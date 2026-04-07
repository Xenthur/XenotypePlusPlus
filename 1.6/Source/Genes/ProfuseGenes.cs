using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace XenotypePlusPlus
{
  [HarmonyPatch]
  public static class ProfuseGenes
  {
    [HarmonyPatch(typeof(GeneUtility), nameof(GeneUtility.ExtractXenogerm))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> GeneUtility_ExtractXenogerm_Patch(IEnumerable<CodeInstruction> instructions)
    {
      var hediffDefMethod = AccessTools.Method(typeof(ProfuseGenes), nameof(ExtractXenogerm_XenogermLossShock));
      var hediffMethod = AccessTools.Method(typeof(ProfuseGenes), nameof(ExtractXenogerm_XenogermReplicating));

      foreach (CodeInstruction instruction in instructions)
      {
        if (instruction.opcode == OpCodes.Callvirt && instruction.operand is MethodInfo methodInfo &&
                    methodInfo.Name == "AddHediff")
        {
          yield return new CodeInstruction(OpCodes.Ldarg_0);
          if (methodInfo.GetParameters()[0].ParameterType == typeof(HediffDef))
          {
            yield return new CodeInstruction(OpCodes.Call, hediffDefMethod);
          }
          else
          {
            yield return new CodeInstruction(OpCodes.Call, hediffMethod);
          }
          yield return new CodeInstruction(OpCodes.Pop);
        }
        else
        {
          yield return instruction;
        }
      }
    }

    [HarmonyPatch(typeof(GeneUtility), nameof(GeneUtility.ReimplantXenogerm))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> GeneUtility_ReimplantXenogerm_Patch(IEnumerable<CodeInstruction> instructions)
    {
      var comaMethod = AccessTools.Method(typeof(ProfuseGenes), nameof(ReimplantXenogerm_Coma));

      foreach (CodeInstruction instruction in instructions)
      {
        if (instruction.opcode == OpCodes.Callvirt && instruction.operand is MethodInfo methodInfo &&
                    methodInfo.Name == "AddHediff")
        {
          yield return new CodeInstruction(OpCodes.Ldarg_1);
          yield return new CodeInstruction(OpCodes.Call, comaMethod);
          yield return new CodeInstruction(OpCodes.Pop);
        }
        else
        {
          yield return instruction;
        }
      }
    }

    [HarmonyPatch(typeof(GeneUtility), nameof(GeneUtility.UpdateXenogermReplication))]
    [HarmonyPostfix]
    public static void GeneUtility_UpdateXenogermReplication_Patch(Pawn pawn)
    {
      if (pawn.HasProfuseGenes())
      {
        Hediff firstHediffOfDef = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.XenogermReplicating);
        if (firstHediffOfDef != null)
        {
          firstHediffOfDef.TryGetComp<HediffComp_Disappears>().ticksToDisappear /= 2;
        }
      }
    }

    [HarmonyPatch(typeof(CompAbilityEffect_ReimplantXenogerm), nameof(CompAbilityEffect_ReimplantXenogerm.Apply), [typeof(LocalTargetInfo), typeof(LocalTargetInfo)])]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> CompAbilityEffect_ReimplantXenogerm_Apply_Patch(IEnumerable<CodeInstruction> instructions)
    {
      var letterMethod = AccessTools.Method(typeof(ProfuseGenes), nameof(SendLetter));
      var parentField = AccessTools.Field(typeof(AbilityComp), nameof(AbilityComp.parent));
      var pawnField = AccessTools.Field(typeof(Ability), nameof(Ability.pawn));
      bool skip = false;

      foreach (CodeInstruction instruction in instructions)
      {
        if (skip)
        {
          if (instruction.opcode == OpCodes.Ret)
          {
            yield return instruction;
            skip = false;
          }
        }
        else if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo methodInfo && methodInfo.Name == "get_LetterStack")
        {
          yield return new CodeInstruction(OpCodes.Ldarg_0);
          yield return new CodeInstruction(OpCodes.Ldfld, parentField);
          yield return new CodeInstruction(OpCodes.Ldfld, pawnField);
          yield return new CodeInstruction(OpCodes.Ldloc_0);
          yield return new CodeInstruction(OpCodes.Ldloc_1);
          yield return new CodeInstruction(OpCodes.Ldloc_2);
          yield return new CodeInstruction(OpCodes.Call, letterMethod);

          skip = true;
        }
        else
        {
          yield return instruction;
        }
      }
    }

    public static Hediff ExtractXenogerm_XenogermLossShock(HediffDef def, BodyPartRecord part, DamageInfo? dinfo, DamageWorker.DamageResult result, Pawn pawn)
    {
      if (pawn.HasProfuseGenes() && def.defName == "XenogermLossShock")
      {
        return null;
      }

      return pawn.health.AddHediff(def, part, dinfo, result);
    }

    public static void ExtractXenogerm_XenogermReplicating(Hediff hediff, BodyPartRecord part, DamageInfo? dinfo, DamageWorker.DamageResult result, Pawn pawn)
    {
      if (pawn.HasProfuseGenes() && hediff.def.defName == "XenogermReplicating")
      {
        hediff.TryGetComp<HediffComp_Disappears>().ticksToDisappear /= 2;
      }

      pawn.health.AddHediff(hediff, part, dinfo, result);
    }

    public static void SendLetter(Pawn caster, Pawn target, int comaMax, int shockMax)
    {
      TaggedString letter = "LetterTextGenesImplantedBase".Translate(caster.Named("SOURCE"), target.Named("RECIPIENT"));
      if (!target.HasProfuseGenes())
      {
        letter += "LetterTextGenesImplantedComa".Translate(target.Named("RECIPIENT"), comaMax.ToStringTicksToPeriod().Named("COMADURATION"));
      }
      if (!caster.HasProfuseGenes())
      {
        letter += "LetterTextGenesImplantedShock".Translate(caster.Named("SOURCE"), shockMax.ToStringTicksToPeriod().Named("SHOCKDURATION"));
      }
      else
      {
        letter += "LetterTextGenesImplantedNoShock".Translate(caster.Named("SOURCE"));
      }
      letter += "LetterTextGenesImplantedEnd".Translate(caster.Named("SOURCE"));

      Find.LetterStack.ReceiveLetter("LetterLabelGenesImplanted".Translate(), letter, LetterDefOf.NeutralEvent, new LookTargets(caster, target));
    }

    public static Hediff ReimplantXenogerm_Coma(HediffDef def, BodyPartRecord part, DamageInfo? dinfo, DamageWorker.DamageResult result, Pawn pawn)
    {
      if (pawn.HasProfuseGenes() && def.defName == "XenogerminationComa")
      {
        return null;
      }

      return pawn.health.AddHediff(def, part, dinfo, result);
    }

    public static bool HasProfuseGenes(this Pawn pawn)
    {
      return pawn.genes.HasActiveGene(DefDatabase<GeneDef>.GetNamedSilentFail("XPP_ProfuseGenes"));
    }
  }
}
