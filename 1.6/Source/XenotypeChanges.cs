using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using static HarmonyLib.Code;
using static Unity.IO.LowLevel.Unsafe.AsyncReadManagerMetrics;

namespace XenotypePlusPlus
{
  public class GermlineCompProperties : CompProperties
  {
    public GermlineCompProperties()
    {
      compClass = typeof(GermlineComp);
    }
  }

  public class GermlineComp : ThingComp
  {
    public GermlineCompProperties Props => (GermlineCompProperties)props;
    private XenotypeDef germline;
    public string germlineName;
    public XenotypeIconDef iconDef;
    public bool hybrid;
    [Unsaved(false)]
    private CustomXenotype cachedCustomXenotype;

    [Unsaved(false)]
    private bool? cachedHasCustomXenotype;

    public bool UniqueXenotype => !germlineName.NullOrEmpty();

    public string XenotypeLabel
    {
      get
      {
        if (!UniqueXenotype)
        {
          return germline.label;
        }
        return germlineName ?? ((string)"Unique".Translate());
      }
    }

    public string XenotypeLabelCap => XenotypeLabel.CapitalizeFirst();

    public Texture2D XenotypeIcon
    {
      get
      {
        if (!ModsConfig.BiotechActive)
        {
          return null;
        }
        if (iconDef != null)
        {
          return iconDef.Icon;
        }
        if (UniqueXenotype)
        {
          return XenotypeIconDefOf.Basic.Icon;
        }
        return germline.Icon;
      }
    }

    public CustomXenotype CustomXenotype
    {
      get
      {
        if (parent is not Pawn pawn)
        {
          return null;
        }
        if (germline.IsCopyable() || hybrid || Current.ProgramState != ProgramState.Playing)
        {
          return null;
        }
        if (!cachedHasCustomXenotype.HasValue)
        {
          cachedHasCustomXenotype = false;
          foreach (CustomXenotype customXenotype in Current.Game.customXenotypeDatabase.customXenotypes)
          {
            if (customXenotype.inheritable && GeneUtility.PawnIsCustomXenotype(pawn, customXenotype))
            {
              cachedHasCustomXenotype = true;
              cachedCustomXenotype = customXenotype;
              break;
            }
          }
        }
        return cachedCustomXenotype;
      }
    }

    public void SetGermline(XenotypeDef newGermline)
    {
      germline = newGermline;
      germlineName = null;
      iconDef = null;
    }

    public void FindGermline()
    {
      if (parent is not Pawn pawn)
      {
        return;
      }
      if (pawn.genes.Xenotype.inheritable)
      {
        germline = pawn.genes.Xenotype;
        germlineName = null;
        iconDef = null;
      }
      else
      {
        germline ??= XenotypeDefOf.Baseliner;
      }
    }

    public override void PostExposeData()
    {
      base.PostExposeData();

      if (Scribe.mode == LoadSaveMode.Saving)
      {
        if (germline == null)
        {
          FindGermline();
        }
        Scribe_Defs.Look(ref germline, "germline");
        Scribe_Values.Look(ref germlineName, "germlineName");
        Scribe_Values.Look(ref hybrid, "hybrid", defaultValue: false);
        Scribe_Defs.Look(ref iconDef, "iconDef");
      }
      else if (Scribe.mode == LoadSaveMode.LoadingVars)
      {
        Scribe_Defs.Look(ref germline, "germline");
        Scribe_Values.Look(ref germlineName, "germlineName");
        Scribe_Values.Look(ref hybrid, "hybrid", defaultValue: false);
        Scribe_Defs.Look(ref iconDef, "iconDef");
        if (germline == null)
        {
          FindGermline();
        }
      }
    }

    public XenotypeDef Germline
    {
      get
      {
        if (germline == null)
        {
          FindGermline();
        }

        return germline;
      }
    }

    public string XenotypeDescShort
    {
      get
      {
        if (UniqueXenotype)
        {
          return "UniqueXenotypeDesc".Translate();
        }
        if (!germline.descriptionShort.NullOrEmpty())
        {
          return germline.descriptionShort + "\n\n" + "MoreInfoInInfoScreen".Translate().Colorize(ColoredText.SubtleGrayColor);
        }
        return germline.description;
      }
    }

    public void TryUseCustom(CustomXenotype custom)
    {
      if (custom != null && custom.inheritable)
      {
        germlineName = custom.name;
        iconDef = custom.iconDef;
      }
    }

    public GermlineComp Clone()
    {
      return new GermlineComp
      {
        germline = this.germline,
        germlineName = this.germlineName,
        iconDef = this.iconDef,
        hybrid = this.hybrid,
        cachedCustomXenotype = this.cachedCustomXenotype,
        cachedHasCustomXenotype = this.cachedHasCustomXenotype
      };
    }
  }


  [HarmonyPatch]
  public static class XenotypeChanges
  {
    

    [HarmonyPatch(typeof(GeneUIUtility), nameof(GeneUIUtility.DrawGenesInfo))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> DrawGenesInfoPatch(IEnumerable<CodeInstruction> instructions)
    {
      var drawMethod = AccessTools.Method(typeof(XenotypeChanges), nameof(TryDrawGermline));

      foreach (CodeInstruction instruction in instructions)
      {
        if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo methodInfo && methodInfo.DeclaringType == typeof(BiostatsTable) && methodInfo.Name == nameof(BiostatsTable.Draw))
        {
          yield return new CodeInstruction(OpCodes.Pop);
          yield return new CodeInstruction(OpCodes.Ldarg_1);
          yield return new CodeInstruction(OpCodes.Call, drawMethod);
        }
        else
        {
          yield return instruction;
        }
      }
    }

    public static void TryDrawGermline(Rect tableRect, int gcx, int met, int arc, bool drawMax, bool ignoreLimits, Thing target)
    {
      if (target is not Pawn pawn)
      {
        return;
      }

      bool drawGermline = pawn.SeparateGermline(out GermlineComp germlineData);

      if (drawGermline)
      {
        tableRect.width -= 140f;
        Rect germlineRect = new(tableRect.xMax + 4f, tableRect.y + Text.LineHeight / 2f, 140f, Text.LineHeight);
        Text.Anchor = TextAnchor.UpperCenter;
        Widgets.Label(germlineRect, germlineData.XenotypeLabelCap);
        Text.Anchor = TextAnchor.UpperLeft;
        Rect position = new(germlineRect.center.x - 17f, germlineRect.yMax + 4f, 34f, 34f);
        GUI.color = XenotypeDef.IconColor;
        GUI.DrawTexture(position, germlineData.XenotypeIcon);
        GUI.color = Color.white;
        germlineRect.yMax = position.yMax;
        if (Mouse.IsOver(germlineRect))
        {
          Widgets.DrawHighlight(germlineRect);
          TooltipHandler.TipRegion(germlineRect, () => ("Xenotype".Translate() + ": " + germlineData.XenotypeLabelCap).Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + germlineData.XenotypeDescShort, 883938493);
        }
        if (Widgets.ButtonInvisible(germlineRect) && !germlineData.UniqueXenotype)
        {
          Find.WindowStack.Add(new Dialog_InfoCard(germlineData.Germline));
        }
      }

      BiostatsTable.Draw(tableRect, gcx, met, arc, drawMax, ignoreLimits);

      if (drawGermline)
      {
        tableRect.width += 140f;
      }

    }

    [HarmonyPatch(typeof(PawnGenerator), "GenerateGenes")]
    [HarmonyPostfix]
    public static void InitCustomXenotype(Pawn pawn, PawnGenerationRequest request)
    {
      if (request.ForcedCustomXenotype != null)
      {
        pawn.GetComp<GermlineComp>().TryUseCustom(request.ForcedCustomXenotype);
      }
    }

    [HarmonyPatch(typeof(GameComponent_PawnDuplicator), nameof(GameComponent_PawnDuplicator.Duplicate))]
    [HarmonyPostfix]
    public static void DuplicateGermline(Pawn pawn, Pawn __result)
    {
      GermlineComp existing = __result.GetComp<GermlineComp>();
      if (existing == null)
      {
        __result.AllComps.Add(pawn.GetComp<GermlineComp>().Clone());
      }
      else
      {
        __result.AllComps.Replace(__result.GetComp<GermlineComp>(), pawn.GetComp<GermlineComp>().Clone());
      }
    }

    public static bool TryNoXenogerm(Pawn_GeneTracker genes, XenotypeDef xenotype)
    {
      if (xenotype == XPPDefs.NoXenogerm)
      {
        genes.RemoveXenogerm();
        return true;
      }
      else
      {
        return false;
      }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), nameof(Pawn_GeneTracker.SetXenotype))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> PatchSetXenotype(IEnumerable<CodeInstruction> instructions, ILGenerator il)
    {
      var validateMethod = AccessTools.Method(typeof(XenotypeChanges), nameof(KeepXenotype));
      var germlineMethod = AccessTools.Method(typeof(XenotypeChanges), nameof(UpdateGermline));
      var tryNoXenogermMethod = AccessTools.Method(typeof(XenotypeChanges), nameof(TryNoXenogerm));

      bool flag = false;
      bool flag2 = false;
      bool flag3 = false;
      Label label = il.DefineLabel();
      Label label2 = il.DefineLabel();
      List<CodeInstruction> codes = [.. instructions];
      for (int i = 0; i < codes.Count; i++)
      {
        if (!flag && codes[i].opcode == OpCodes.Ldarg_0)
        {
          flag = true;
          yield return codes[i];
          yield return new CodeInstruction(OpCodes.Ldarg_1);
          yield return new CodeInstruction(OpCodes.Call, tryNoXenogermMethod);
          yield return new CodeInstruction(OpCodes.Brfalse, label);
          yield return new CodeInstruction(OpCodes.Ret);
          yield return new CodeInstruction(OpCodes.Nop).WithLabels(label);
          yield return new CodeInstruction(OpCodes.Ldarg_0);
        }
        else if (!flag2 && codes[i].opcode == OpCodes.Ldarg_1)
        {
          flag2 = true;
          yield return codes[i];
          yield return new CodeInstruction(OpCodes.Call, validateMethod);
          yield return new CodeInstruction(OpCodes.Brtrue, label2);
          yield return new CodeInstruction(OpCodes.Ldarg_0);
          yield return new CodeInstruction(OpCodes.Ldarg_1);
        }
        else if (!flag3 && i + 1 < codes.Count && codes[i + 1].opcode == OpCodes.Call && codes[i + 1].operand is MethodInfo methodInfo && methodInfo.Name == nameof(Pawn_GeneTracker.ClearXenogenes))
        {
          flag3 = true;
          yield return new CodeInstruction(OpCodes.Nop).WithLabels(label2);
          yield return codes[i];
        }
        else if (codes[i].opcode == OpCodes.Blt_S)
        {
          yield return codes[i];
          yield return new CodeInstruction(OpCodes.Ldarg_0);
          yield return new CodeInstruction(OpCodes.Ldarg_1);
          yield return new CodeInstruction(OpCodes.Call, germlineMethod);
        }
        else
        {
          yield return codes[i];
        }
      }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), nameof(Pawn_GeneTracker.SetXenotypeDirect))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> PatchSetXenotypeDirect(IEnumerable<CodeInstruction> instructions, ILGenerator il)
    {
      var validateMethod = AccessTools.Method(typeof(XenotypeChanges), nameof(KeepXenotype));
      var germlineMethod = AccessTools.Method(typeof(XenotypeChanges), nameof(UpdateGermline));
      var tryNoXenogermMethod = AccessTools.Method(typeof(XenotypeChanges), nameof(TryNoXenogerm));

      bool flag = false;
      bool flag2 = false;
      Label label = il.DefineLabel();
      Label label2 = il.DefineLabel();
      List<CodeInstruction> codes = [.. instructions];
      int flag3 = 0;

      for (int i = 0; i < codes.Count; i++)
      {
        if (!flag && codes[i].opcode == OpCodes.Ldarg_0)
        {
          flag = true;
          yield return new CodeInstruction(OpCodes.Ldarg_0);
          yield return new CodeInstruction(OpCodes.Ldarg_1);
          yield return new CodeInstruction(OpCodes.Call, tryNoXenogermMethod);
          yield return new CodeInstruction(OpCodes.Brfalse, label);
          yield return new CodeInstruction(OpCodes.Ret);
          yield return new CodeInstruction(OpCodes.Nop).WithLabels(label);
          yield return codes[i];
        }
        else if (!flag && codes[i].opcode == OpCodes.Ldarg_1)
        {
          flag = true;
          yield return codes[i];
          yield return new CodeInstruction(OpCodes.Call, validateMethod);
          yield return new CodeInstruction(OpCodes.Brtrue, label2);
          yield return new CodeInstruction(OpCodes.Ldarg_0);
          yield return new CodeInstruction(OpCodes.Ldarg_1);
        }
        else if (!flag2 && codes[i].opcode == OpCodes.Ret)
        {
          flag2 = true;
          yield return new CodeInstruction(OpCodes.Nop).WithLabels(label2);
          yield return codes[i];
        }
        else if (codes[i].opcode == OpCodes.Stfld)
        {
          yield return codes[i];
          flag3++;
          if (flag3 == 3)
          {
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Ldarg_1);
            yield return new CodeInstruction(OpCodes.Call, germlineMethod);
          }
        }
        else
        {
          yield return codes[i];
        }
      }

    }

    private static void UpdateXenogerm(int pawnIndex, CustomXenotype customXenotype)
    {
      forcedXenogerms[pawnIndex] = null;
      forcedCustomXenogerms[pawnIndex] = customXenotype;
      allowedXenogerms[pawnIndex] = null;
    }

    //[HarmonyDebug]
    [HarmonyPatch(typeof(Dialog_CreateXenotype), "AcceptInner")]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> PatchCreateXenotype(IEnumerable<CodeInstruction> instructions, ILGenerator il)
    {
      var updateMethod = AccessTools.Method(typeof(XenotypeChanges), nameof(UpdateXenogerm));

      bool flag = false;
      bool flag2 = false;
      Label label = il.DefineLabel();
      Label label2 = il.DefineLabel();

      foreach (CodeInstruction instruction in instructions)
      {
        yield return instruction;
        if (!flag && instruction.opcode == OpCodes.Stloc_2)
        {
          flag = true;
          yield return new CodeInstruction(OpCodes.Ldarg_0);
          yield return CodeInstruction.LoadField(typeof(Dialog_CreateXenotype), "inheritable");
          yield return new CodeInstruction(OpCodes.Brtrue, label);

          yield return new CodeInstruction(OpCodes.Ldarg_0);
          yield return CodeInstruction.LoadField(typeof(Dialog_CreateXenotype), "generationRequestIndex");

          Type targetInnerType = AccessTools.FirstInner(typeof(Dialog_CreateXenotype), (type) =>
            type.GetDeclaredFields().Where((FieldInfo field) => field.Name == "customXenotype").Any());

          yield return new CodeInstruction(OpCodes.Ldloc_0);
          yield return CodeInstruction.LoadField(targetInnerType, "customXenotype");
          yield return new CodeInstruction(OpCodes.Call, updateMethod);

          yield return new CodeInstruction(OpCodes.Br, label2);
          yield return new CodeInstruction(OpCodes.Nop).WithLabels(label);
        }
        else if (!flag2 && instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo methodInfo && methodInfo.Name == "SetGenerationRequest")
        {
          flag2 = true;
          yield return new CodeInstruction(OpCodes.Nop).WithLabels(label2);
        }
      }
    }

    public static void UpdateGermline(Pawn_GeneTracker __instance, XenotypeDef xenotype)
    {
      GermlineComp germlineData = __instance.pawn.GetComp<GermlineComp>();
      if (germlineData == null)
      {
        return;
      }

      if (xenotype.IsGermline())
      {
        germlineData.SetGermline(xenotype);
      }
      else
      {
        germlineData.FindGermline();
      }
    }

    public static bool KeepXenotype(Pawn_GeneTracker genes, XenotypeDef xenotype)
    {
      return xenotype.IsGermline() && !genes.Xenotype.IsGermline();
    }

    [HarmonyPatch(typeof(CharacterCardUtility), "DoTopStack")]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> DrawGermlineStack(IEnumerable<CodeInstruction> instructions)
    {
      int target = -1;
      var method = AccessTools.Method(typeof(XenotypeChanges), nameof(AddGermlineStack));

      List<CodeInstruction> codes = [.. instructions];
      for (int i = 0; i < codes.Count; i++)
      {
        if (codes[i].opcode == OpCodes.Callvirt && codes[i].operand is MethodInfo methodInfo && methodInfo.DeclaringType == typeof(Pawn_GeneTracker) && methodInfo.Name == "get_GenesListForReading")
        {
          target = i + 3;
        }
      }

      if (target > 0)
      {
        codes.InsertRange(target, [
          new CodeInstruction(OpCodes.Ldarg_0),
          CodeInstruction.LoadField(typeof(CharacterCardUtility), "tmpStackElements"),
          new CodeInstruction(OpCodes.Call, method)
        ]);
      }

      return codes;
    }

    private static void AddGermlineStack(Pawn pawn, List<GenUI.AnonymousStackElement> tmpStackElements)
    {
      if (!pawn.SeparateGermline(out GermlineComp germlineData))
      {
        return;
      }
      float num2 = 22f;
      num2 += Text.CalcSize(germlineData.XenotypeLabelCap).x + 14f;
      tmpStackElements.Add(new GenUI.AnonymousStackElement
      {
        drawer = delegate (Rect r)
        {
          Rect rect2 = new(r.x, r.y, r.width, r.height);
          GUI.color = CharacterCardUtility.StackElementBackground;
          GUI.DrawTexture(rect2, BaseContent.WhiteTex);
          GUI.color = Color.white;
          if (Mouse.IsOver(rect2))
          {
            Widgets.DrawHighlight(rect2);
          }
          Rect position = new(r.x + 1f, r.y + 1f, 20f, 20f);
          GUI.color = XenotypeDef.IconColor;
          GUI.DrawTexture(position, germlineData.XenotypeIcon);
          GUI.color = Color.white;
          Widgets.Label(new Rect(r.x + 22f + 5f, r.y, r.width + 22f - 1f, r.height), germlineData.XenotypeLabelCap);
          if (Mouse.IsOver(r))
          {
            TooltipHandler.TipRegion(r, () => ("Xenotype".Translate() + ": " + germlineData.XenotypeLabelCap).Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + germlineData.XenotypeDescShort + "\n\n" + "ViewGenesDesc".Translate(pawn.Named("PAWN")).ToString().StripTags()
              .Colorize(ColoredText.SubtleGrayColor), 883938493);
          }
          if (Widgets.ButtonInvisible(r))
          {
            if (Current.ProgramState == ProgramState.Playing && Find.WindowStack.WindowOfType<Dialog_InfoCard>() == null && Find.WindowStack.WindowOfType<Dialog_GrowthMomentChoices>() == null)
            {
              InspectPaneUtility.OpenTab(typeof(ITab_Genes));
            }
            else
            {
              Find.WindowStack.Add(new Dialog_ViewGenes(pawn));
            }
          }
        },
        width = num2
      });
    }

    //[HarmonyPatch(typeof(Pawn_GeneTracker), "get_CustomXenotype")]
    //[HarmonyPrefix]
    //public static bool PatchGetCustomXenotype(Pawn_GeneTracker __instance, ref CustomXenotype __result)
    //{
    //  var cachedHasCustomXenotype = AccessTools.FieldRefAccess<Pawn_GeneTracker, bool?>("cachedHasCustomXenotype");
    //  var cachedCustomXenotype = AccessTools.FieldRefAccess<Pawn_GeneTracker, CustomXenotype>("cachedCustomXenotype");

    //  if (__instance.Xenotype != XenotypeDefOf.Baseliner || __instance.hybrid || Current.ProgramState != ProgramState.Playing)
    //  {
    //    __result = null;
    //  }
    //  if (!cachedHasCustomXenotype(__instance).HasValue)
    //  {
    //    cachedHasCustomXenotype(__instance) = false;
    //    foreach (CustomXenotype customXenotype in Current.Game.customXenotypeDatabase.customXenotypes.Where((CustomXenotype x) => !x.inheritable))
    //    {
    //      if (GeneUtility.PawnIsCustomXenotype(__instance.pawn, customXenotype))
    //      {
    //        cachedHasCustomXenotype(__instance) = true;
    //        cachedCustomXenotype(__instance) = customXenotype;
    //        break;
    //      }
    //    }

    //    if (!cachedHasCustomXenotype(__instance).HasValue || !cachedHasCustomXenotype(__instance).Value)
    //    {
    //      foreach (CustomXenotype customXenotype in Current.Game.customXenotypeDatabase.customXenotypes.Where((CustomXenotype x) => x.inheritable))
    //      {
    //        if (GeneUtility.PawnIsCustomXenotype(__instance.pawn, customXenotype))
    //        {
    //          cachedHasCustomXenotype(__instance) = true;
    //          cachedCustomXenotype(__instance) = customXenotype;
    //          break;
    //        }
    //      }
    //    }
    //  }
    //  __result = cachedCustomXenotype(__instance);

    //  return false;
    //}

    //[HarmonyPatch(typeof(GeneUtility), nameof(GeneUtility.PawnIsCustomXenotype))]
    //[HarmonyPostfix]
    //public static void debugShit(Pawn pawn, CustomXenotype custom)
    //{
    //  if (!ModsConfig.BiotechActive || pawn.genes == null)
    //  {
    //    Log.Error(pawn.Name + " is not a " + custom.name + " because no genes");
    //  }
    //  List<Gene> list = (custom.inheritable ? pawn.genes.Endogenes : pawn.genes.Xenogenes);
    //  int i;
    //  for (i = 0; i < custom.genes.Count; i++)
    //  {
    //    if (custom.genes[i].passOnDirectly && !list.Any((Gene x) => x.def == custom.genes[i]))
    //    {
    //      Log.Error(pawn.Name + " is not a " + custom.name + " because he is missing this gene: " + custom.genes[i].defName);
    //    }
    //  }
    //  for (int j = 0; j < list.Count; j++)
    //  {
    //    if (list[j].def.passOnDirectly && !custom.genes.Contains(list[j].def))
    //    {
    //      Log.Error(pawn.Name + " is not a " + custom.name + " because this gene does not belong: " + list[j].def.defName);
    //    }
    //  }
    //  Log.Error(pawn.Name + " is a " + custom.name);
    //}

    [HarmonyPatch(typeof(Ideo), nameof(Ideo.IsPreferredXenotype))]
    [HarmonyPostfix]
    public static void IsGermlinePreferredXenotype(Ideo __instance, ref bool __result, Pawn pawn)
    {
      if (__result)
      {
        return;
      }

      if (!__instance.PreferredXenotypes.Any() && !__instance.PreferredCustomXenotypes.Any())
      {
        return;
      }
      GermlineComp germlineData = pawn.GetComp<GermlineComp>();
      if (germlineData == null || (germlineData.Germline == null && germlineData.germlineName == null))
      {
        return;
      }
      if (germlineData.CustomXenotype != null)
      {
        __result = __instance.PreferredCustomXenotypes.Contains(germlineData.CustomXenotype);
      }
      __result = __result || __instance.PreferredXenotypes.Contains(germlineData.Germline);
    }

    [HarmonyPatch(typeof(CharacterCardUtility), nameof(CharacterCardUtility.DrawCharacterCard))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> ExpandRandomizedButton(IEnumerable<CodeInstruction> instructions)
    {
      bool flag = false;
      int flag2 = 0;
      List<CodeInstruction> codes = [.. instructions];
      for (int i = 0; i < codes.Count; i++)
      {
        if (codes[i].opcode == OpCodes.Ldloca_S && codes[i].LocalIndex() == 18)
        {
          flag = true;
        }
        else if (flag && flag2 < 2 && codes[i].opcode == OpCodes.Ldc_R4 && codes[i].operand is float num && num == 200f)
        {
          codes[i] = new CodeInstruction(OpCodes.Ldc_R4, 302f);
          flag2++;
        }
      }

      return codes;
    }

    private static List<XenotypeDef> forcedXenogerms = [];
    private static List<CustomXenotype> forcedCustomXenogerms = [];
    private static List<List<XenotypeDef>> allowedXenogerms = [];

    // Temp variables to account for warnedChangingXenotypeWillRandomizePawn
    private static List<XenotypeDef> newForcedXenogerms = [];
    private static List<CustomXenotype> newForcedCustomXenogerms = [];
    private static List<List<XenotypeDef>> newAllowedXenogerms = [];

    private static Texture2D GetXenotypeIcon(int startingPawnIndex)
    {
      if (forcedXenogerms[startingPawnIndex] != null)
      {
        return forcedXenogerms[startingPawnIndex].Icon;
      }
      if (forcedCustomXenogerms[startingPawnIndex] != null)
      {
        return forcedCustomXenogerms[startingPawnIndex].IconDef.Icon;
      }
      if (allowedXenogerms[startingPawnIndex] != null)
      {
        return GeneUtility.UniqueXenotypeTex.Texture;
      }
      return XPPDefs.NoXenogerm.Icon;
    }

    private static string GetXenotypeLabel(int startingPawnIndex)
    {
      if (forcedXenogerms[startingPawnIndex] != null)
      {
        return forcedXenogerms[startingPawnIndex].LabelCap;
      }
      if (forcedCustomXenogerms[startingPawnIndex] != null)
      {
        return forcedCustomXenogerms[startingPawnIndex].name.CapitalizeFirst();
      }
      if (allowedXenogerms[startingPawnIndex] != null)
      {
        return "AnyLower".Translate().CapitalizeFirst();
      }
      return "NoXenogerm".Translate().CapitalizeFirst();
    }

    public static IEnumerable<XenotypeDef> GermlineXenotypes(IEnumerable<XenotypeDef> xenotypes)
    {
      return xenotypes.Where((XenotypeDef x) => x.IsGermline());
    }

    private static List<CustomXenotype> GermlineCustomXenotypes(List<CustomXenotype> xenotypes)
    {
      return [.. xenotypes.Where((CustomXenotype x) => x.inheritable)];
    }

    [HarmonyPatch(typeof(CharacterCardUtility), "LifestageAndXenotypeOptions")]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> UpdateXenotypeButtons(IEnumerable<CodeInstruction> instructions)
    {
      var filterMethod = AccessTools.Method(typeof(XenotypeChanges), nameof(GermlineXenotypes));
      var customFilterMethod = AccessTools.Method(typeof(XenotypeChanges), nameof(GermlineCustomXenotypes));

      bool flag = false;
      List<CodeInstruction> codes = [.. instructions];
      for (int i = 0; i < codes.Count; i++)
      {
        if (!flag && codes[i].opcode == OpCodes.Stloc_1 && i >= 4)
        {
          codes[i - 4] = new CodeInstruction(OpCodes.Ldc_R4, 8f);
          codes[i - 2] = new CodeInstruction(OpCodes.Ldc_R4, 3f);

          flag = true;
        }
        else if (codes[i].opcode == OpCodes.Call && codes[i].operand is MethodInfo methodInfo && methodInfo.Name == "get_AllDefs")
        {
          codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, filterMethod));
        }
        else if (codes[i].opcode == OpCodes.Call && codes[i].operand is MethodInfo methodInfo2 && methodInfo2.Name == "get_CustomXenotypes")
        {
          codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, customFilterMethod));
        }
      }

      return codes;
    }


    [HarmonyPatch(typeof(StartingPawnUtility), "EnsureGenerationRequestInRangeOf")]
    [HarmonyPostfix]
    public static void EnsureXenogerms(int index)
    {
      while (forcedXenogerms.Count <= index)
      {
        forcedXenogerms.Add(null);
      }
      while (forcedCustomXenogerms.Count <= index)
      {
        forcedCustomXenogerms.Add(null);
      }
      while (allowedXenogerms.Count <= index)
      {
        allowedXenogerms.Add(null);
      }

      while (newForcedXenogerms.Count <= index)
      {
        newForcedXenogerms.Add(null);
      }
      while (newForcedCustomXenogerms.Count <= index)
      {
        newForcedCustomXenogerms.Add(null);
      }
      while (newAllowedXenogerms.Count <= index)
      {
        newAllowedXenogerms.Add(null);
      }
    }

    [HarmonyPatch(typeof(StartingPawnUtility), nameof(StartingPawnUtility.ReorderRequests))]
    [HarmonyPostfix]
    public static void ReorderXenogerms(int from, int to)
    {
      forcedXenogerms.ReorderItem(from, to);
      forcedCustomXenogerms.ReorderItem(from, to);
      allowedXenogerms.ReorderItem(from, to);
    }

    [HarmonyPatch(typeof(StartingPawnUtility), nameof(StartingPawnUtility.RandomizePawn))]
    [HarmonyPostfix]
    private static void AddXenogerm(int pawnIndex)
    {
      if (TutorSystem.AllowAction("RandomizePawn"))
      {
        Pawn pawn = Find.GameInitData.startingAndOptionalPawns[pawnIndex];
        pawn.genes.ClearXenogenes();
        if (forcedXenogerms[pawnIndex] != null)
        {
          pawn.genes.SetXenotype(forcedXenogerms[pawnIndex]);
        }
        else if (forcedCustomXenogerms[pawnIndex] != null)
        {
          pawn.genes.xenotypeName = forcedCustomXenogerms[pawnIndex].name;
          pawn.genes.iconDef = forcedCustomXenogerms[pawnIndex].IconDef;
          foreach (GeneDef gene in forcedCustomXenogerms[pawnIndex].genes)
          {
            pawn.genes.AddGene(gene, !forcedCustomXenogerms[pawnIndex].inheritable);
          }
        }
        else if (allowedXenogerms[pawnIndex] != null && allowedXenogerms[pawnIndex].TryRandomElement(out var result))
        {
          pawn.genes.SetXenotype(result);
        }
      }
    }

    [HarmonyPatch(typeof(CharacterCardUtility), "SetupGenerationRequest")]
    [HarmonyPrefix]
    public static void AddBaselinerToRandom(ref List<XenotypeDef> allowedXenotypes)
    {
      if (allowedXenotypes != null && allowedXenotypes.Count > 0)
      {
        allowedXenotypes.AddUnique(XenotypeDefOf.Baseliner);
      }
    }

    public static void ConfirmNewXenogerms(int pawnIndex)
    {
      EnsureXenogerms(pawnIndex);
      forcedXenogerms[pawnIndex] = newForcedXenogerms[pawnIndex];
      forcedCustomXenogerms[pawnIndex] = newForcedCustomXenogerms[pawnIndex];
      allowedXenogerms[pawnIndex] = newAllowedXenogerms[pawnIndex];
    }

    [HarmonyPatch(typeof(StartingPawnUtility), nameof(StartingPawnUtility.SetGenerationRequest))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> PatchSetupGenerationRequest(IEnumerable<CodeInstruction> instructions)
    {
      var confirmMethod = AccessTools.Method(typeof(XenotypeChanges), nameof(ConfirmNewXenogerms));

      yield return new CodeInstruction(OpCodes.Ldarg_0);
      yield return new CodeInstruction(OpCodes.Call, confirmMethod);
      foreach (CodeInstruction instruction in instructions)
      {
        yield return instruction;
      }
    }

    [HarmonyPatch(typeof(CharacterCardUtility), "LifestageAndXenotypeOptions")]
    [HarmonyPostfix]
    public static void ExtendXenotypeOptions(Pawn pawn, Rect randomizeRect, bool creationMode, bool allowsChildSelection, Action randomizeCallback)
    {
      float width = (randomizeRect.width - 8f) / 3f;
      float x2 = randomizeRect.x + 2 * width + 8f;
      int startingPawnIndex = StartingPawnUtility.PawnIndex(pawn);
      PawnGenerationRequest existing = StartingPawnUtility.GetGenerationRequest(startingPawnIndex);

      string xenotypeLabel = GetXenotypeLabel(startingPawnIndex);
      Texture2D xenotypeIcon = GetXenotypeIcon(startingPawnIndex);
      var setupGenerationRequest = AccessTools.Method(typeof(CharacterCardUtility), "SetupGenerationRequest");



      Rect rect4 = new Rect(x2, randomizeRect.y + randomizeRect.height + 4f, width, randomizeRect.height);
      Text.Anchor = TextAnchor.MiddleCenter;
      Rect rect5 = rect4;
      rect5.y += rect4.height + 4f;
      rect5.height = Text.LineHeight;
      Widgets.Label(rect5, xenotypeLabel.Truncate(rect5.width));
      Text.Anchor = TextAnchor.UpperLeft;
      Rect rect6 = new Rect(rect4.x, rect4.y, rect4.width, rect5.yMax - rect4.yMin);
      if (Mouse.IsOver(rect6))
      {
        Widgets.DrawHighlight(rect6);
        if (Find.WindowStack.FloatMenu == null)
        {
          TooltipHandler.TipRegion(rect6, xenotypeLabel.Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + "XenotypeSelectionDesc".Translate());
        }
      }
      if (!Widgets.ButtonImageWithBG(rect4, xenotypeIcon, new Vector2(22f, 22f)) || !TutorSystem.AllowAction("ChangeXenotype"))
      {
        return;
      }
      int index2 = startingPawnIndex;
      List<FloatMenuOption> list = new List<FloatMenuOption>
      {
        new FloatMenuOption("AnyNonArchite".Translate().CapitalizeFirst(), delegate
        {
          List<XenotypeDef> allowedXenotypes = DefDatabase<XenotypeDef>.AllDefs.Where((XenotypeDef x) => !x.Archite && x != XenotypeDefOf.Baseliner && !x.inheritable).ToList();
          EnsureXenogerms(startingPawnIndex);
          forcedXenogerms[startingPawnIndex] = null;
          forcedCustomXenogerms[startingPawnIndex] = null;
          allowedXenogerms[startingPawnIndex] = allowedXenotypes;
          newForcedXenogerms[startingPawnIndex] = null;
          newForcedCustomXenogerms[startingPawnIndex] = null;
          newAllowedXenogerms[startingPawnIndex] = allowedXenotypes;
          setupGenerationRequest.Invoke(null, [index2, existing.ForcedXenotype, existing.ForcedCustomXenotype, existing.AllowedXenotypes, 0.5f, (PawnGenerationRequest existing) => allowedXenogerms[startingPawnIndex] == null, randomizeCallback, false]);
        }),
        new FloatMenuOption("XenotypeEditor".Translate() + "...", delegate
        {
          Find.WindowStack.Add(new Dialog_CreateXenotype(index2, delegate
          {
            CharacterCardUtility.cachedCustomXenotypes = null;
            randomizeCallback();
          }));
        }, MenuOptionPriority.Default, delegate (Rect r)
        {
          TooltipHandler.TipRegion(r, "No xenogerm will be applied.");
        }, null, 24f, null, null, playSelectionSound: true, 0),
        new FloatMenuOption("NoXenogerm".Translate().CapitalizeFirst(), delegate
        {
          EnsureXenogerms(startingPawnIndex);
          bool allowChange = false;
          if (forcedXenogerms[startingPawnIndex] != null || forcedCustomXenogerms[startingPawnIndex] != null || allowedXenogerms[startingPawnIndex] != null)
          {
            forcedXenogerms[startingPawnIndex] = null;
            forcedCustomXenogerms[startingPawnIndex] = null;
            allowedXenogerms[startingPawnIndex] = null;
            allowChange = true;
          }
          newForcedXenogerms[startingPawnIndex] = null;
          newForcedCustomXenogerms[startingPawnIndex] = null;
          newAllowedXenogerms[startingPawnIndex] = null;
          setupGenerationRequest.Invoke(null, [index2, existing.ForcedXenotype, existing.ForcedCustomXenotype, existing.AllowedXenotypes, 0.5f, (PawnGenerationRequest existing) => allowChange, randomizeCallback, true]);
        }),
      };
      foreach (XenotypeDef item in DefDatabase<XenotypeDef>.AllDefs.Where((XenotypeDef x) => x != XenotypeDefOf.Baseliner && x != XPPDefs.NoXenogerm && !x.inheritable).OrderBy((XenotypeDef x) => 0f - x.displayPriority))
      {
        XenotypeDef xenotype2 = item;
        list.Add(new FloatMenuOption(xenotype2.LabelCap, delegate
        {
          EnsureXenogerms(startingPawnIndex);
          newForcedXenogerms[startingPawnIndex] = xenotype2;
          newForcedCustomXenogerms[startingPawnIndex] = null;
          newAllowedXenogerms[startingPawnIndex] = null;
          setupGenerationRequest.Invoke(null, [index2, existing.ForcedXenotype, existing.ForcedCustomXenotype, existing.AllowedXenotypes, 0f, (PawnGenerationRequest existing) => XenotypeValidator(existing, xenotype2, startingPawnIndex), randomizeCallback, true]);
        }, xenotype2.Icon, XenotypeDef.IconColor, MenuOptionPriority.Default, delegate (Rect r)
        {
          TooltipHandler.TipRegion(r, xenotype2.descriptionShort ?? xenotype2.description);
        }, null, 24f, (Rect r) => Widgets.InfoCardButton(r.x, r.y + 3f, xenotype2) ? true : false, null, playSelectionSound: true, 0, HorizontalJustification.Left, extraPartRightJustified: true));
      }
      foreach (CustomXenotype customXenotype in CharacterCardUtility.CustomXenotypesForReading.Where((CustomXenotype x) => !x.inheritable))
      {
        CustomXenotype customInner = customXenotype;
        list.Add(new FloatMenuOption(customInner.name.CapitalizeFirst() + " (" + "Custom".Translate() + ")", delegate
        {
          EnsureXenogerms(startingPawnIndex);
          newForcedXenogerms[startingPawnIndex] = null;
          newForcedCustomXenogerms[startingPawnIndex] = customInner;
          newAllowedXenogerms[startingPawnIndex] = null;
          setupGenerationRequest.Invoke(null, [index2, existing.ForcedXenotype, existing.ForcedCustomXenotype, existing.AllowedXenotypes, 0f, (PawnGenerationRequest existing) => CustomXenotypeValidator(existing, customInner, startingPawnIndex), randomizeCallback, true]);
        }, customInner.IconDef.Icon, XenotypeDef.IconColor, MenuOptionPriority.Default, null, null, 24f, delegate (Rect r)
        {
          if (Widgets.ButtonImage(new Rect(r.x, r.y + (r.height - r.width) / 2f, r.width, r.width), TexButton.Delete, GUI.color))
          {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmDelete".Translate(customInner.name.CapitalizeFirst()), delegate
            {
              string path = GenFilePaths.AbsFilePathForXenotype(customInner.name);
              if (File.Exists(path))
              {
                File.Delete(path);
                CharacterCardUtility.cachedCustomXenotypes = null;
              }
            }, destructive: true));
            return true;
          }
          return false;
        }, null, playSelectionSound: true, 0, HorizontalJustification.Left, extraPartRightJustified: true));
      }
      Find.WindowStack.Add(new FloatMenu(list));
      static bool CustomXenotypeValidator(PawnGenerationRequest req, CustomXenotype xenotype, int startingPawnIndex)
      {
        if (TutorSystem.TutorialMode && req.MustBeCapableOfViolence && xenotype.genes.Any((GeneDef g) => g.disabledWorkTags.HasFlag(WorkTags.Violent)))
        {
          Messages.Message("MessageStartingPawnCapableOfViolence".Translate(), MessageTypeDefOf.RejectInput, historical: false);
          return false;
        }
        return forcedCustomXenogerms[startingPawnIndex] != xenotype;
      }
      static bool XenotypeValidator(PawnGenerationRequest req, XenotypeDef xenotype, int startingPawnIndex)
      {
        if (TutorSystem.TutorialMode && req.MustBeCapableOfViolence && xenotype.AllGenes.Any((GeneDef g) => g.disabledWorkTags.HasFlag(WorkTags.Violent)))
        {
          Messages.Message("MessageStartingPawnCapableOfViolence".Translate(), MessageTypeDefOf.RejectInput, historical: false);
          return false;
        }
        return forcedXenogerms[startingPawnIndex] != xenotype;
      }
    }
  }


  [HarmonyPatch]
  public class CharacterCardUtilityPatch
  {
    public static MethodBase TargetMethod()
    {
      return AccessTools.FindIncludingInnerTypes(typeof(CharacterCardUtility), (type) =>
        AccessTools.FirstMethod(type, (method) => method.IsAssembly && PatchProcessor.ReadMethodBody(method).Where((KeyValuePair<OpCode, object> instruction) =>
        instruction.Key == OpCodes.Call && instruction.Value is MethodInfo methodInfo && methodInfo.Name == "get_AllDefs").Any()));
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> UpdateXenotypeButtons(IEnumerable<CodeInstruction> instructions)
    {
      var filterMethod = AccessTools.Method(typeof(XenotypeChanges), nameof(XenotypeChanges.GermlineXenotypes));

      List<CodeInstruction> codes = [.. instructions];
      for (int i = 0; i < codes.Count; i++)
      {
        if (codes[i].opcode == OpCodes.Call && codes[i].operand is MethodInfo methodInfo && methodInfo.Name == "get_AllDefs")
        {
          codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, filterMethod));
        }
      }

      return codes;
    }
  }
}
