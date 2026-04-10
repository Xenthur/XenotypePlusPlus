using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml.Linq;
using Verse;

namespace XenotypePlusPlus
{
  [HarmonyPatch]
  public static class GainXenotypePatch
  {
    public static HashSet<Pawn> pawnsToUpdate = [];

    public static MethodBase TargetMethod()
    {
      return AccessTools.Method(typeof(Pawn), "Tick");
    }

    [HarmonyPostfix]
    public static void SetXenotypeFromGene(Pawn __instance)
    {
      if (!pawnsToUpdate.Contains(__instance))
      {
        return;
      }
      Pawn_GeneTracker genes = __instance.genes;
      List<XenotypeDef> germlines = [.. genes.GenesListForReading.Where((g) => g is GainXenotypeGene xg && xg.GetGeneXenotype() != null && xg.GetGeneXenotype().IsGermline()).Select((g) => ((GainXenotypeGene)g).GetGeneXenotype())];
      List<XenotypeDef> xenogerms = [.. genes.GenesListForReading.Where((g) => g is GainXenotypeGene xg && (xg.GetGeneXenotype() == null || !xg.GetGeneXenotype().IsGermline())).Select((g) => ((GainXenotypeGene)g).GetGeneXenotype())];
      XenotypeDef currentXenotype = genes.Xenotype;

      if (!germlines.Empty())
      {
        XenotypeDef selectedGermline = germlines.RandomElement();
        if (currentXenotype.IsGermline())
        {
          genes.SetXenotypeDirect(selectedGermline);
          genes.iconDef = null;
        }
        else
        {
          genes.pawn.GetComp<GermlineComp>().SetGermline(selectedGermline);
        }

        GeneDef currentHairGene = genes.GetHairColorGene();
        GeneDef currentMelaninGene = genes.GetMelaninGene();
        for (int num = genes.Endogenes.Count - 1; num >= 0; num--)
        {
          if (genes.Endogenes[num].def != currentMelaninGene)
          {
            genes.RemoveGene(genes.Endogenes[num]);
          }
        }
        for (int i = 0; i < selectedGermline.genes.Count; i++)
        {
          genes.AddGene(selectedGermline.genes[i], false);
        }

        if (genes.GetHairColorGene() == null)
        {
          currentHairGene ??= PawnHairColors.ClosestHairColorGene(__instance.story.HairColor, __instance.story.SkinColorBase);
          if (currentHairGene != null)
          {
            genes.AddGene(currentHairGene, xenogene: false);
          }
        }
        if (!genes.Endogenes.ContainsAny((Gene g) => g.def.skinColorOverride.HasValue) && !genes.Endogenes.ContainsAny((Gene g) => g.def.skinIsHairColor) && currentMelaninGene == null)
        {
          GeneDef newMelaninGene = PawnSkinColors.RandomSkinColorGene(__instance);
          if (newMelaninGene != null)
          {
            genes.AddGene(newMelaninGene, xenogene: false);
          }
        }
      }

      if (!xenogerms.Empty())
      {
        XenotypeDef selectedXenogerm = xenogerms.RandomElement();
        if (selectedXenogerm == null)
        {
          genes.RemoveXenogerm();
        }
        else
        {
          genes.SetXenotype(selectedXenogerm);
        }
      }

      foreach (Gene gene in genes.GenesListForReading.Where((g) => g is GainXenotypeGene xg))
      {
        gene.RemoveAllOfThisGene();
      }

      pawnsToUpdate.Remove(__instance);
    }
  }

  public class GainXenotypeGene : Gene
  {
    private GainXenotypeExtension extension;

    public override void Tick()
    {
      GainXenotypePatch.pawnsToUpdate.Add(pawn);
    }

    public XenotypeDef GetGeneXenotype()
    {
      extension = def.GetModExtension<GainXenotypeExtension>() ?? new GainXenotypeExtension();
      return extension.xenotype;
    }

  }

  public class GainXenotypeExtension(XenotypeDef xenotype) : DefModExtension
  {
    public XenotypeDef xenotype = xenotype;

    public GainXenotypeExtension() : this(XenotypeDefOf.Baseliner)
    {

    }
  }

  [HarmonyPatch]
  public static class GainXenotypeDefGenerator
  {
    public static readonly CachedTexture germline = new("GeneIcons/XPP_GainGermline");
    public static readonly CachedTexture xenogerm = new("GeneIcons/XPP_GainXenogerm");

    [HarmonyPatch(typeof(GeneDefGenerator), nameof(GeneDefGenerator.ImpliedGeneDefs))]
    [HarmonyPriority(Priority.VeryLow)]
    [HarmonyPostfix]
    public static void ImpliedGeneDefs_Postfix(ref IEnumerable<GeneDef> __result)
    {
      var resultList = __result.ToList();

      foreach (var geneDef in GenerateXenotypeGenes())
      {
        geneDef.geneClass = typeof(GainXenotypeGene);
        resultList.Add(geneDef);
      }
      __result = resultList;
    }

    public static List<GeneDef> GenerateXenotypeGenes()
    {
      var result = new List<GeneDef>();
      var allXenotypes = DefDatabase<XenotypeDef>.AllDefsListForReading;

      var gainTemplate = DefDatabase<GeneTemplate>.GetNamed("XPP_GainXenotypeTemplate");
      var nxgTemplate = DefDatabase<GeneTemplate>.GetNamed("XPP_NoXenogermTemplate");
      if (gainTemplate == null)
      {
        Log.Warning("XenotypePlusPlus DefGen: GenerateXenotypeGenes: Could not find the Gain Xenotype Template. Gain Xenotype genes will not be generated.");
      }
      else
      {
        try
        {
          foreach (var xeno in allXenotypes)
          {
            result.Add(GenerateGainXenotypeGene(xeno, gainTemplate));
          }
          result.Add(GenerateNoXenogermGene(nxgTemplate));
        }
        catch (Exception e)
        {
          Log.Error($"Exception duing XenotypePlusPlus DefGen: GenerateXenotypeGenes.\nGenerating the genes has been aborted.\n{e.Message}\n{e.StackTrace}");
        }
      }


      return result;
    }

    [HarmonyPatch(typeof(GeneUIUtility), "DrawGeneBasics")]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> DrawGainXenotype(IEnumerable<CodeInstruction> instructions)
    {
      bool backgroundPatched = false;
      List<CodeInstruction> codes = [.. instructions];
      for (int i = 1; i < codes.Count; i++)
      {
        bool runBackgroundPatch = !backgroundPatched && i > 3 && i < codes.Count - 2 &&
           codes[i].IsLdloc() && codes[i].operand is LocalBuilder lb && lb.LocalIndex == 4 &&
           (codes[i + 1].opcode == OpCodes.Callvirt && codes[i + 1].OperandIs(typeof(CachedTexture).GetMethod("get_Texture")));

        if (runBackgroundPatch)
        {
          backgroundPatched = true;
          List<CodeInstruction> newInstructions =
          [
            new CodeInstruction(OpCodes.Ldloc_S, 4), // Load cachedTexture
            new CodeInstruction(OpCodes.Ldarg_0), // Load GeneDef
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GainXenotypeDefGenerator), nameof(GetXenotypeGeneBackground))),
            new CodeInstruction(OpCodes.Stloc_S, 4), // Store cachedTexture
          ];
          codes.InsertRange(i, newInstructions);
        }
      }

      return codes;

    }

    public static GeneDef GenerateGainXenotypeGene(XenotypeDef xenoDef, GeneTemplate template)
    {
      if (xenoDef == null || template == null)
      {
        Log.Error($"XenotypePlusPlus DefGen: GenerateXenotypeGene: One of the parameters was null." +
            $"\nXenoDef: {xenoDef}, template: {template}");
        return null;
      }

      string defName = $"{xenoDef.defName}_{template.keyTag}";
      string xenotypeCat = xenoDef.IsGermline() ? "germline" : "xenogerm";
      string geneCat = xenoDef.IsGermline() ? "endogenes" : "xenogenes";

      var geneDef = new GeneDef
      {
        defName = defName,
        label = string.Format(template.label, xenoDef.label, xenotypeCat),
        description = string.Format(template.description, xenoDef.label, xenotypeCat, geneCat),
        customEffectDescriptions = [string.Format(template.customEffectDescriptions[0], xenoDef.label, xenotypeCat)],
        iconPath = xenoDef.iconPath,
        biostatCpx = 0,
        biostatMet = 0,
        displayCategory = template.displayCategory,
        canGenerateInGeneSet = template.canGenerateInGeneSet,
        selectionWeight = template.selectionWeight,
        modExtensions = [new GainXenotypeExtension(xenoDef)]
      };

      return geneDef;
    }

    public static GeneDef GenerateNoXenogermGene(GeneTemplate template)
    {
      if (template == null)
      {
        Log.Error($"XenotypePlusPlus DefGen: GenerateXenotypeGene: One of the parameters was null." +
            $"\ntemplate: {template}");
        return null;
      }

      string defName = $"XPP_NoXenogerm_Gene";

      var geneDef = new GeneDef
      {
        defName = defName,
        label = string.Format(template.label, "no xenogerm"),
        description = string.Format(template.description, "xenogerm"),
        customEffectDescriptions = [template.customEffectDescriptions[0]],
        iconPath = "XenotypeIcons/XPP_NoXenogerm",
        biostatCpx = 0,
        biostatMet = 0,
        displayCategory = template.displayCategory,
        canGenerateInGeneSet = template.canGenerateInGeneSet,
        selectionWeight = template.selectionWeight,
        modExtensions = [new GainXenotypeExtension(null)]
      };

      return geneDef;
    }

    public static CachedTexture GetXenotypeGeneBackground(CachedTexture previous, GeneDef gene)
    {
      if (gene.geneClass == typeof(GainXenotypeGene))
      {
        var extension = gene.GetModExtension<GainXenotypeExtension>() ?? new GainXenotypeExtension();
        return extension.xenotype != null && extension.xenotype.IsGermline() ? germline : xenogerm;
      }
      return previous;
    }
  }
}
