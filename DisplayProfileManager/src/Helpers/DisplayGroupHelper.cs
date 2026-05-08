using DisplayProfileManager.Core;
using System.Collections.Generic;
using System.Linq;

namespace DisplayProfileManager.Helpers
{
    public static class DisplayGroupingHelper
    {
        public class DisplayGroup
        {
            public DisplaySetting RepresentativeSetting { get; set; }
            public List<DisplaySetting> AllMembers { get; set; }
            public bool IsCloneGroup => AllMembers.Count > 1;
        }

        public static List<DisplayGroup> GroupDisplaysForUI(List<DisplaySetting> displaySettings)
        {
            var result = new List<DisplayGroup>();

            // Identify existing clone relationships
            var cloneGroups = displaySettings
                .Where(s => s.IsPartOfCloneGroup())
                .GroupBy(s => s.CloneGroupId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var processedCloneGroups = new HashSet<string>();

            foreach (var setting in displaySettings)
            {
                // Synchronize group processing state
                if (setting.IsPartOfCloneGroup() && processedCloneGroups.Contains(setting.CloneGroupId))
                {
                    continue;
                }

                if (setting.IsPartOfCloneGroup())
                {
                    processedCloneGroups.Add(setting.CloneGroupId);
                }

                // Resolve member collection
                var members = setting.IsPartOfCloneGroup()
                    ? cloneGroups[setting.CloneGroupId]
                    : new List<DisplaySetting> { setting };

                result.Add(new DisplayGroup
                {
                    RepresentativeSetting = setting,
                    AllMembers = members
                });
            }

            return result;
        }
    }
}