using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace XenotypePlusPlus
{
  public static class Util
  {
    public static void RemoveAllOfThisGene(this Gene gene)
    {
      gene.pawn.genes.Xenogenes.RemoveAll((Gene g) => g.def == gene.def);
      gene.pawn.genes.Endogenes.RemoveAll((Gene g) => g.def == gene.def);
      AccessTools.FieldRefAccess<Pawn_GeneTracker, List<Gene>>("cachedGenes")(gene.pawn.genes) = null;
    }

    public static string GeneListNatural(this List<GeneDef> genes)
    {
      string geneMessage = genes[0].label;
      if (genes.Count > 1)
      {
        if (genes.Count == 2)
        {
          geneMessage += " and " + genes[1].label;
        }
        else
        {
          for (int i = 1; i < genes.Count - 1; i++)
          {
            geneMessage += ", " + genes[i].label;
          }
          geneMessage += ", and " + genes.Last().label;
        }
      }

      return geneMessage;
    }

    public static bool IsGermline(this XenotypeDef xenotype)
    {
      return xenotype.inheritable || xenotype == XenotypeDefOf.Baseliner;
    }

    private static readonly List<XenotypeDef> uncopyable = [XenotypeDefOf.Baseliner, XPPDefs.NoXenogerm];

    public static bool IsCopyable(this XenotypeDef xenotype)
    {
      return !uncopyable.Contains(xenotype);
    }

    public static bool SeparateGermline(this Pawn pawn, out GermlineComp germlineData, bool ignoreSettings = false)
    {
      germlineData = pawn.GetComp<GermlineComp>();
      if (germlineData.CustomXenotype != null)
      {
        return germlineData.CustomXenotype != pawn.genes.CustomXenotype;
      }
      if (germlineData.Germline == XenotypeDefOf.Baseliner && germlineData.germlineName == null && (ignoreSettings || XenotypePlusPlus.settings.hideBaselinerGermline))
      {
        return false;
      }
      return germlineData.Germline != pawn.genes.Xenotype || germlineData.germlineName != pawn.genes.xenotypeName;
    }

    public static void RemoveXenogerm(this Pawn_GeneTracker genes)
    {
      genes.ClearXenogenes();
      if ((genes.CustomXenotype != null && !genes.CustomXenotype.inheritable) || !genes.Xenotype.IsGermline())
      {
        GermlineComp germlineData = genes.pawn.GetComp<GermlineComp>();
        var xenotype = AccessTools.FieldRefAccess<Pawn_GeneTracker, XenotypeDef>("xenotype");
        //var xenotypeName = AccessTools.FieldRefAccess<Pawn_GeneTracker, string>("xenotypeName");
        //var xenotypeIcon = AccessTools.FieldRefAccess<Pawn_GeneTracker, XenotypeIconDef>("iconDef");
        var cachedHasCustomXenotype = AccessTools.FieldRefAccess<Pawn_GeneTracker, bool?>("cachedHasCustomXenotype");
        var cachedCustomXenotype = AccessTools.FieldRefAccess<Pawn_GeneTracker, CustomXenotype>("cachedCustomXenotype");

        xenotype(genes) = germlineData.Germline ?? XenotypeDefOf.Baseliner;
        genes.xenotypeName = germlineData.germlineName;
        genes.iconDef = germlineData.iconDef;
        cachedHasCustomXenotype(genes) = null;
        cachedCustomXenotype(genes) = null;

        _ = genes.CustomXenotype;
      }
    }

    public static void ReorderItem<T>(this List<T> list, int from, int to)
    {
      T temp = list[from];
      list.Insert(to, temp);
      list.RemoveAt((from < to) ? from : (from + 1));
    }
  }

  [DefOf]
  public static class XPPDefs
  {
    [MayRequire("Xenthur.XenotypePlusPlus")]
    public static XenotypeDef NoXenogerm;

    static XPPDefs()
    {
      DefOfHelper.EnsureInitializedInCtor(typeof(Util));
    }
  }

  public abstract class RandomGeneGene : Gene
  {
    public RandomGeneExtension extension;

    protected bool AddRandomGenes(bool message = false)
    {
      int geneCount = extension.geneCount.RandomInRange;
      List<GeneDef> addedGenes = [];

      for (int i = 0; i < geneCount; i++)
      {
        int totalMet = pawn.genes.GenesListForReading.Where((Gene g) => g.Active).Aggregate(0, (sum, g) => sum + g.def.biostatMet);
        List<GeneDef> validGenes = [.. DefDatabase<GeneDef>.AllDefsListForReading.Where((g) => AddRandomGeneFilter(g, totalMet))];
        if (validGenes.Empty())
        {
          break;
        }
        GeneDef newGene = validGenes.RandomElement();
        pawn.genes.AddGene(newGene, extension.isXeno);
        addedGenes.Add(newGene);
      }

      if (message && !addedGenes.Empty())
      {
        Messages.Message("GainedRandomGene".Translate(pawn.Named("SUBJECT"), addedGenes.GeneListNatural().Named("GENES"), (addedGenes.Count > 1 ? "s" : "").Named("PLURAL")), pawn, MessageTypeDefOf.NeutralEvent);
      }

      return !addedGenes.Empty();
    }

    protected bool RemoveRandomGenes(bool message = false)
    {
      int geneCount = extension.geneCount.RandomInRange;
      List<GeneDef> removedGenes = [];

      for (int i = 0; i < geneCount; i++)
      {
        int totalMet = pawn.genes.GenesListForReading.Where((Gene g) => g.Active).Aggregate(0, (sum, g) => sum + g.def.biostatMet);
        List<Gene> validGenes = [.. pawn.genes.GenesListForReading.Where((g) => RemoveRandomGeneFilter(g, totalMet))];
        if (validGenes.Empty())
        {
          break;
        }
        Gene newGene = validGenes.RandomElement();
        pawn.genes.RemoveGene(newGene);
        removedGenes.Add(newGene.def);
      }

      if (message && !removedGenes.Empty())
      {
        Messages.Message("LostRandomGene".Translate(pawn.Named("SUBJECT"), removedGenes.GeneListNatural().Named("GENES"), (removedGenes.Count > 1 ? "s" : "").Named("PLURAL")), pawn, MessageTypeDefOf.NeutralEvent);
      }

      return !removedGenes.Empty();
    }

    private bool AddRandomGeneFilter(GeneDef g, int totalMet)
    {
      return
        g.biostatArc <= 0 &&
        g.biostatMet >= GeneTuning.BiostatRange.TrueMin - totalMet &&
        g.biostatMet <= GeneTuning.BiostatRange.TrueMax - totalMet &&
        g.geneClass != typeof(GainXenotypeGene) &&
        g.passOnDirectly &&
        g.canGenerateInGeneSet &&
        !extension.excludedGenes.Contains(g) &&
        (extension.isXeno ? !pawn.genes.HasXenogene(g) : !pawn.genes.HasEndogene(g)) &&
        (g.prerequisite == null || pawn.genes.HasEndogene(g.prerequisite) || (extension.isXeno && pawn.genes.HasXenogene(g.prerequisite)));
    }

    private bool RemoveRandomGeneFilter(Gene g, int totalMet)
    {
      GeneDef def = g.def;

      return
        def.biostatArc <= 0 &&
        (!g.Active ||
        (totalMet >= GeneTuning.BiostatRange.TrueMin + def.biostatMet &&
        totalMet <= GeneTuning.BiostatRange.TrueMax + def.biostatMet)) &&
        def.geneClass != typeof(GainXenotypeGene) &&
        def.passOnDirectly &&
        def.canGenerateInGeneSet &&
        !extension.excludedGenes.Contains(def) &&
        !pawn.genes.GenesListForReading.Any((Gene other) => other.def.prerequisite == def);
    }
  }

  public class RandomGeneExtension : DefModExtension
  {
    public bool isXeno = false;
    public IntRange geneCount = new(1, 10);
    public List<GeneDef> excludedGenes = [];
  }

  public class GeneTemplate : Def
  {
    public GeneCategoryDef displayCategory;
    public float selectionWeight = 1f;
    public bool canGenerateInGeneSet = true;
    public string keyTag = "";
    public List<string> customEffectDescriptions = [];
  }
}
