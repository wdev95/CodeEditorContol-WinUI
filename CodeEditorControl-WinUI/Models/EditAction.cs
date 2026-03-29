using System.Collections.Generic;

namespace CodeEditorControl_WinUI;
/// <summary>Represents a single edit operation for undo/redo.</summary>
public class EditAction
{
   public EditActionType EditActionType { get; set; }
   public string TextInvolved { get; set; }
   public Range Selection { get; set; }
   public int AffectedStartLine { get; set; } = -1;
   public List<string> OldLines { get; set; }
   public List<string> NewLines { get; set; }
   public List<string> SavedTexts { get; set; }
   public override string ToString() => TextInvolved;
}
