namespace CodeEditorControl_WinUI;
/// <summary>Represents a search result match at a specific position.</summary>
public class SearchMatch
{
	public int iChar { get; set; }
	public int iLine { get; set; }
	public string Match { get; set; }
}
