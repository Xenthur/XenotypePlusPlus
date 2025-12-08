using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace XenotypePlusPlus
{
  public class CompPropertiesExtractXenogerm : CompProperties_AbilityEffect
  {
    public List<GeneDef> genesToRetain = [];
    public CompPropertiesExtractXenogerm()
    {
      compClass = typeof(CompAbilityExtractXenogerm);
    }
  }

  public class CompAbilityExtractXenogerm : CompAbilityEffect
  {
    private new CompPropertiesExtractXenogerm Props => (CompPropertiesExtractXenogerm)props;

    public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
    {
      base.Apply(target, dest);
      Pawn pawn = target.Pawn;
      if (pawn != null)
      {
        CopyGenes(parent.pawn, pawn, genesToRetain: Props.genesToRetain);
        FleckMaker.AttachedOverlay(parent.pawn, FleckDefOf.FlashHollow, new Vector3(0f, 0f, 0.26f));
        if (PawnUtility.ShouldSendNotificationAbout(parent.pawn) || PawnUtility.ShouldSendNotificationAbout(pawn))
        {
          int comaMax = HediffDefOf.XenogerminationComa.CompProps<HediffCompProperties_Disappears>().disappearsAfterTicks.max;
          int shockMax = HediffDefOf.XenogermLossShock.CompProps<HediffCompProperties_Disappears>().disappearsAfterTicks.max;

          TaggedString letter = "LetterTextGenesExtractedBase".Translate(parent.pawn.Named("RECIPIENT"), pawn.Named("SOURCE"));
          if (!parent.pawn.HasProfuseGenes())
          {
            letter += "LetterTextGenesImplantedComa".Translate(parent.pawn.Named("RECIPIENT"), comaMax.ToStringTicksToPeriod().Named("COMADURATION"));
          }
          if (!pawn.HasProfuseGenes())
          {
            letter += "LetterTextGenesImplantedShock".Translate(pawn.Named("SOURCE"), shockMax.ToStringTicksToPeriod().Named("SHOCKDURATION"));
          }
          else
          {
            letter += "LetterTextGenesImplantedNoShock".Translate(pawn.Named("SOURCE"));
          }
          letter += "LetterTextGenesExtractedEnd".Translate(pawn.Named("SOURCE"));

          Find.LetterStack.ReceiveLetter("LetterLabelGenesExtracted".Translate(), letter, LetterDefOf.NeutralEvent, new LookTargets(parent.pawn, pawn));
        }
      }
    }

    public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
    {
      Pawn pawn = target.Pawn;
      if (pawn == null)
      {
        return base.Valid(target, throwMessages);
      }
      if (pawn.IsQuestLodger())
      {
        if (throwMessages)
        {
          Messages.Message("MessageCannotExtractFromTempFactionMembers".Translate(), pawn, MessageTypeDefOf.RejectInput, historical: false);
        }
        return false;
      }
      if (pawn.HostileTo(parent.pawn) && !pawn.Downed)
      {
        if (throwMessages)
        {
          Messages.Message("MessageCantUseOnResistingPerson".Translate(parent.def.Named("ABILITY")), pawn, MessageTypeDefOf.RejectInput, historical: false);
        }
        return false;
      }
      if (pawn.genes == null || !pawn.genes.GenesListForReading.Any((Gene x) => x.def.passOnDirectly))
      {
        if (throwMessages)
        {
          Messages.Message("PawnHasNoGenes".Translate(pawn), pawn, MessageTypeDefOf.RejectInput, historical: false);
        }
        return false;
      }
      if (GeneUtility.SameXenotype(pawn, parent.pawn))
      {
        if (throwMessages)
        {
          Messages.Message("MessageCannotUseOnSameXenotype".Translate(parent.pawn), parent.pawn, MessageTypeDefOf.RejectInput, historical: false);
        }
        return false;
      }
      if (!PawnIdeoCanAcceptReimplant(pawn, parent.pawn))
      {
        if (throwMessages)
        {
          Messages.Message("MessageCannotBecomeNonPreferredXenotype".Translate(parent.pawn), parent.pawn, MessageTypeDefOf.RejectInput, historical: false);
        }
        return false;
      }
      return base.Valid(target, throwMessages);
    }

    public override Window ConfirmationDialog(LocalTargetInfo target, Action confirmAction)
    {
      if (GeneUtility.PawnWouldDieFromReimplanting(target.Pawn))
      {
        return Dialog_MessageBox.CreateConfirmation("ConfirmExtractXenogermWillKill".Translate(target.Named("PAWN")), confirmAction, destructive: true);
      }
      return null;
    }

    public override IEnumerable<Mote> CustomWarmupMotes(LocalTargetInfo target)
    {
      yield return MoteMaker.MakeAttachedOverlay(parent.pawn, ThingDefOf.Mote_XenogermImplantation, new Vector3(0f, 0f, 0.3f));
    }

    public static bool PawnIdeoCanAcceptReimplant(Pawn implanter, Pawn implantee)
    {
      if (!ModsConfig.IdeologyActive)
      {
        return true;
      }
      if (!IdeoUtility.DoerWillingToDo(HistoryEventDefOf.PropagateBloodfeederGene, implantee) && implanter.genes.Xenogenes.Any((Gene x) => x.def == GeneDefOf.Bloodfeeder))
      {
        return false;
      }
      if (!IdeoUtility.DoerWillingToDo(HistoryEventDefOf.BecomeNonPreferredXenotype, implantee) && !implantee.Ideo.IsPreferredXenotype(implanter))
      {
        return false;
      }

      return true;
    }

    public static List<Gene> GetGenesToCopy(Pawn pawn, out XenotypeDef copiedXenotype, out string copiedXenotypeName, out XenotypeIconDef copiedXenotypeIcon)
    {
      List<Gene> result = new List<Gene>();
      var genes = pawn.genes.GenesListForReading;
      GeneDef xenoCarrierGeneDef = DefDatabase<GeneDef>.GetNamedSilentFail("BF_XenoCarrier");
      for (int i = 0; i < genes.Count; i++)
      {
        if ((genes[i].Active || genes[i].overriddenByGene.def == xenoCarrierGeneDef) && genes[i].def.endogeneCategory != EndogeneCategory.Melanin)
        {
          result.Add(genes[i]);
        }
      }

      copiedXenotype = null;
      copiedXenotypeName = null;
      copiedXenotypeIcon = null;

      bool separateGermline = pawn.SeparateGermline(out GermlineComp germlineData, true);

      if (!separateGermline)
      {
        if (!pawn.genes.Xenotype.IsCopyable())
        {
          copiedXenotype = pawn.genes.Xenotype;
        }
        else if (pawn.genes.xenotypeName != null)
        {
          copiedXenotypeName = pawn.genes.xenotypeName;
          copiedXenotypeIcon = pawn.genes.iconDef;
        }
      }
      Log.Error(copiedXenotypeName);

      return result;
    }

    public static void CopyGenes(Pawn caster, Pawn target, List<GeneDef> genesToRetain)
    {
      QuestUtility.SendQuestTargetSignals(caster.questTags, "XenogermReimplanted", caster.Named("SUBJECT"));
      caster.genes.Xenogenes.RemoveAll(gene => !genesToRetain.Contains(gene.def));

      List<Gene> genesToCopy = GetGenesToCopy(target, out XenotypeDef copiedXenotype, out string copiedXenotypeName, out XenotypeIconDef copiedXenotypeIcon);

      foreach (Gene gene in genesToCopy)
      {
        caster.genes.AddGene(gene.def, xenogene: true);
      }
      if (!caster.genes.Xenotype.soundDefOnImplant.NullOrUndefined())
      {
        caster.genes.Xenotype.soundDefOnImplant.PlayOneShot(SoundInfo.InMap(caster));
      }
      if (!caster.HasProfuseGenes())
      {
        caster.health.AddHediff(HediffDefOf.XenogerminationComa, null, null);
      }

      if (copiedXenotype != null)
      {
        AccessTools.FieldRefAccess<Pawn_GeneTracker, XenotypeDef>("xenotype")(caster.genes) = copiedXenotype;
        caster.genes.xenotypeName = null;
        caster.genes.iconDef = null;
        caster.genes.hybrid = false;
      }
      else if (copiedXenotypeName != null)
      {
        AccessTools.FieldRefAccess<Pawn_GeneTracker, XenotypeDef>("xenotype")(caster.genes) = XenotypeDefOf.Baseliner;
        caster.genes.xenotypeName = copiedXenotypeName;
        caster.genes.iconDef = copiedXenotypeIcon;
        caster.genes.hybrid = false;
      }
      else
      {
        AccessTools.FieldRefAccess<Pawn_GeneTracker, XenotypeDef>("xenotype")(caster.genes) = XenotypeDefOf.Baseliner;
        caster.genes.xenotypeName = "Hybrid".Translate();
        caster.genes.iconDef = null;
        caster.genes.hybrid = true;
      }

      GeneUtility.ExtractXenogerm(target);
      GeneUtility.UpdateXenogermReplication(caster);
    }
  }
}
