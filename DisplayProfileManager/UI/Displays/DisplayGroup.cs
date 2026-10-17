using System.Collections.Generic;

using DisplayProfileManager.Displays.Models;

namespace DisplayProfileManager.UI.Displays
{
    public class DisplayGroup
    {
        public DisplaySetting RepresentativeSetting { get; set; }
        public List<DisplaySetting> AllMembers { get; set; }
        public bool IsCloneGroup => AllMembers.Count > 1;
    }
}
