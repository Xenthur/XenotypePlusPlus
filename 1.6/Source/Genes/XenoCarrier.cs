using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace XenotypePlusPlus
{
  [HarmonyPatch]
  public static class XenoCarrierPatch1
  {
    public static IEnumerable<MethodBase> TargetMethods()
    {
      yield return AccessTools.Method(typeof(Pawn_GeneTracker), "Notify_GenesChanged");
      yield return AccessTools.Method(typeof(Pawn_GeneTracker), "OverrideAllConflicting");
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Pawn_GeneTracker_SelectGene_Patch(IEnumerable<CodeInstruction> instructions)
    {
      foreach (CodeInstruction instruction in instructions)
      {
        if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo methodInfo && methodInfo.Name == "get_GenesListForReading")
        {
          yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(XenoCarrierPatch1), nameof(GetXenoCarrierGeneList)));
        }
        else
        {
          yield return instruction;
        }

      }
    }

    public static List<Gene> GetXenoCarrierGeneList(Pawn_GeneTracker genes)
    {
      if (genes.GetGene(DefDatabase<GeneDef>.GetNamedSilentFail("XPP_XenoCarrier")) != null)
      {
        return genes.Endogenes;
      }
      return genes.GenesListForReading;
    }


  }

  [HarmonyPatch]
  public static class XenoCarrierPatch2
  {
    [HarmonyPatch(typeof(Pawn_GeneTracker), "CheckForOverrides")]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Pawn_GeneTracker_CheckForOverrides_Patch(IEnumerable<CodeInstruction> instructions)
    {
      bool flag = true;

      foreach (CodeInstruction instruction in instructions)
      {
        yield return instruction;

        if (instruction.opcode == OpCodes.Stloc_3 && flag)
        {
          flag = false;

          yield return new CodeInstruction(OpCodes.Ldarg_0);
          yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(XenoCarrierPatch2), nameof(DisableIfXenoCarrier)));
          yield return new CodeInstruction(OpCodes.Stloc_0);
        }
      }
    }

    public static List<Gene> DisableIfXenoCarrier(Pawn_GeneTracker genes)
    {
      GeneDef xenoCarrierGeneDef = DefDatabase<GeneDef>.GetNamedSilentFail("XPP_XenoCarrier");
      Gene xenoCarrierGene = genes.Endogenes.Find(g => g.def == xenoCarrierGeneDef) ?? genes.GetGene(xenoCarrierGeneDef);

      if (xenoCarrierGene != null)
      {
        foreach (Gene gene in genes.Xenogenes)
        {
          if (gene != xenoCarrierGene && gene.def.geneClass != typeof(GainXenotypeGene) && gene.def.geneClass != typeof(GrabBagGene))
          {
            gene.OverrideBy(xenoCarrierGene);
          }
        }
        AccessTools.Field(typeof(Pawn_GeneTracker), "cachedGenes").SetValue(genes, null);

        return genes.Endogenes;
      }

      return genes.GenesListForReading;
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), "AddGene", [typeof(Gene), typeof(bool)])]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Pawn_GeneTracker_AddGene_Patch(IEnumerable<CodeInstruction> instructions, ILGenerator il)
    {
      List<CodeInstruction> codes = [.. instructions];
      int insTarget = -1;
      int brTarget = -1;
      Label label = il.DefineLabel();

      for (int i = 0; i < codes.Count; i++)
      {
        if (codes[i].opcode == OpCodes.Call && codes[i].operand is MethodInfo methodInfo)
        {
          if (methodInfo.Name == "CheckForOverrides")
          {
            insTarget = i + 1;
          }
          else if (methodInfo.Name == "Notify_GenesChanged")
          {
            brTarget = i - 3;
          }
        }
      }
      if (brTarget > insTarget && insTarget >= 0)
      {
        codes.Insert(brTarget, new CodeInstruction(OpCodes.Nop).WithLabels(label));
        codes.InsertRange(insTarget, [new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Gene), nameof(Gene.Overridden))), new CodeInstruction(OpCodes.Brtrue, label)]);
      }

      return codes;
    }
  }
}
