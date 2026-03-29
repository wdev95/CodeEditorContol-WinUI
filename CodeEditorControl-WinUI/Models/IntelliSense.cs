using System.Collections.Generic;

namespace CodeEditorControl_WinUI;
/// <summary>Base type for IntelliSense suggestions.</summary>
public class Suggestion : Bindable
{
	public string Name { get; set; }
	public Token Token { get; set; } = Token.Normal;
	public IntelliSenseType IntelliSenseType { get; set; } = IntelliSenseType.Command;
	public string Snippet { get; set; } = "";
	public string Options { get; set; } = "";
	public string Description { get; set; } = "";
}
/// <summary>An IntelliSense command suggestion with arguments.</summary>
public class IntelliSense : Suggestion
{
	public IntelliSense(string text) { Name = text; Token = Token.Command; }
	public List<Argument> ArgumentsList { get; set; } = new();
}
/// <summary>A command argument with parameters.</summary>
public class Argument : Suggestion
{
	public int Number { get; set; }
	public bool IsSelected { get => Get(false); set => Set(value); }
	public bool Optional { get; set; }
	public string Delimiters { get; set; }
	public string List { get; set; }
	public List<Parameter> Parameters { get; set; }
}
/// <summary>A parameter within an argument.</summary>
public class Parameter : Suggestion { }
/// <summary>A constant parameter with a type descriptor.</summary>
public class Constant : Parameter
{
	public string Type { get; set; }
}
/// <summary>A key-value parameter with possible values.</summary>
public class KeyValue : Parameter
{
	public List<string> Values { get; set; } = new();
}
/// <summary>A matching pair of brackets at two places.</summary>
public class BracketPair
{
	public BracketPair() { }
	/// <summary>Creates a bracket pair from <paramref name="open"/> to <paramref name="close"/>.</summary>
	public BracketPair(Place open, Place close) { iOpen = open; iClose = close; }
	public Place iOpen { get; set; } = new();
	public Place iClose { get; set; } = new();
}
/// <summary>Represents a syntax error or warning at a specific position.</summary>
public class SyntaxError
{
	public string Title { get; set; } = "";
	public string Description { get; set; } = "";
	public int iLine { get; set; }
	public int iChar { get; set; }
	public SyntaxErrorType SyntaxErrorType { get; set; } = SyntaxErrorType.None;
}
