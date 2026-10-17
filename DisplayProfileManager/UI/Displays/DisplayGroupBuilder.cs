using System.Collections.Generic;
using System.Linq;

using DisplayProfileManager.Displays.Models;

namespace DisplayProfileManager.UI.Displays
{
    public static class DisplayGroupBuilder
    {

        public static List<DisplayGroup> GroupDisplaysForUI(List<DisplaySetting> displaySettings)
        {
            var result = new List<DisplayGroup>();

            var cloneGroups = displaySettings.Where(s => s.IsPartOfCloneGroup()).GroupBy(s => s.CloneGroupId).ToDictionary(g => g.Key, g => g.ToList());
            var processedCloneGroups = new HashSet<string>();
            foreach (var setting in displaySettings)
            {
                if (setting.IsPartOfCloneGroup() && processedCloneGroups.Contains(setting.CloneGroupId)) continue;

                if (setting.IsPartOfCloneGroup())
                    processedCloneGroups.Add(setting.CloneGroupId);

                var members = setting.IsPartOfCloneGroup() ? cloneGroups[setting.CloneGroupId] : [setting];
                var representative = members.FirstOrDefault(m => m.IsCloneSource) ?? members.First();
                result.Add(new DisplayGroup
                {
                    RepresentativeSetting = representative,
                    AllMembers = members
                });
            }

            return result;
        }
    }
}
