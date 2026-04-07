using HarmonyLib;
using System.Runtime;
using UnityEngine;
using Verse;

namespace XenotypePlusPlus
{
  [StaticConstructorOnStartup]
  internal class XenotypePlusPlus : Mod
  {
    public static XPPSettings settings;

    public XenotypePlusPlus(ModContentPack content) : base(content)
    {
      Harmony harmony = new("Xenthur.XenotypePlusPlus");
      harmony.PatchAll();
      settings = GetSettings<XPPSettings>();
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
      Listing_Standard listingStandard = new Listing_Standard();
      listingStandard.Begin(inRect);
      listingStandard.CheckboxLabeled("HideBaselinerGermline".Translate(), ref settings.hideBaselinerGermline, "HideBaselinerGermlineToolTip".Translate());
      listingStandard.CheckboxLabeled("AllowPreferredGermline".Translate(), ref settings.allowPreferredGermline, "AllowPreferredGermlineToolTip".Translate());
      listingStandard.CheckboxLabeled("AllowPreferredBaselinerGermline".Translate(), ref settings.allowPreferredBaselinerGermline, "AllowPreferredBaselinerGermlineToolTip".Translate());
      listingStandard.End();
      base.DoSettingsWindowContents(inRect);
    }

    public override string SettingsCategory()
    {
      return "XPPModName".Translate();
    }
  }

  public class XPPSettings : ModSettings
  {
    public bool hideBaselinerGermline;
    public bool allowPreferredGermline = true;
    public bool allowPreferredBaselinerGermline;

    public override void ExposeData()
    {
      Scribe_Values.Look(ref hideBaselinerGermline, "hideBaselinerGermline");
      Scribe_Values.Look(ref allowPreferredGermline, "allowPreferredGermline");
      Scribe_Values.Look(ref allowPreferredBaselinerGermline, "allowPreferredBaselinerGermline");
      base.ExposeData();
    }

    
  }
}