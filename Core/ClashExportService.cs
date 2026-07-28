using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace NavisworksIfcExporter.Core
{
    // The actual CSV export pipeline (snapshot + property/GUID resolution + writer) lives in
    // UI/ClashExportWindow.xaml.cs. This class only provides the clash-tree traversal helpers
    // shared by the window's test list and progress count.
    public static class ClashExportService
    {
        public static IEnumerable<ClashTest> GetAllTests(SavedItemCollection items)
        {
            foreach (SavedItem item in items)
            {
                if (item is ClashTest test)         yield return test;
                else if (item is ClashTestFolder f) foreach (var t in GetAllTests(f.Children)) yield return t;
            }
        }

        public static int CountResults(ClashTest test)
        {
            int n = 0;
            foreach (SavedItem item in test.Children)
            {
                if (item is ClashResult) n++;
                else if (item is ClashResultGroup g)
                    foreach (SavedItem child in g.Children)
                        if (child is ClashResult) n++;
            }
            return n;
        }
    }
}
