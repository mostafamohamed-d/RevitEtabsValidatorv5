using System.Collections.Generic;

namespace RevitEtabsValidator.Revit.UI;

// Compatibility alias for floor-mapping code introduced during the Phase 2 UI work.
// The canonical dictionary is _etabsToRevitLevel; this alias keeps older references
// compiling without changing the mapping behavior.
public partial class MainWindow
{
    private Dictionary<string, string> _revitToEtabsLevel => _etabsToRevitLevel;
}
