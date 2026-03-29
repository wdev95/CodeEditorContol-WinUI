using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeEditorControl_WinUI;
/// <summary>A single tokenized character with a syntax token type.</summary>
public class Char : CharElement
{
	public Char(char c) { C = c; }
}
/// <summary>Base class for tokenized character elements.</summary>
public class CharElement : Bindable
{
	public char C { get => Get(' '); set => Set(value); }
	public Token T { get => Get(Token.Normal); set => Set(value); }
}
/// <summary>Represents a single line of text in the editor.</summary>
public class Line : Bindable
{
	public VisibleState VisibleState = VisibleState.Visible;
	private string lastsavedtext;
	public string SavedText { get => lastsavedtext; set => lastsavedtext = value; }
	public Line(Language language = null) { if (language != null) Language = language; }
	public List<Char> Chars { get => Get(new List<Char>()); set => Set(value); }
	public List<List<Char>> WrappedLines { get => Get(new List<List<Char>>()); set => Set(value); }
	public int Count => Chars.Count;
	public Folding Folding { get => Get(new Folding()); set => Set(value); }
	public string FoldingEndMarker { get; set; }
	public string FoldingStartMarker { get; set; }
	public int iLine => LineNumber - 1;
	public int Indents => LineText?.Count(x => x == '\t') ?? 0;
	public bool IsFoldEnd { get => Get(false); set => Set(value); }
	public bool IsFoldInner { get => Get(false); set => Set(value); }
	public bool IsFoldInnerEnd { get => Get(false); set => Set(value); }
	public bool IsFoldStart { get => Get(false); set => Set(value); }
	public bool IsUnsaved { get => Get(false); set => Set(value); }
	public Language Language { get => Get<Language>(); set { Set(value); SetLineText(LineText); } }
	public int LineNumber { get => Get(0); set => Set(value); }
	public string LineText
	{
		get => Get("");
		set { IsUnsaved = value != lastsavedtext; Set(value); }
	}
	public int WordWrapStringsCount { get; internal set; }
	public Char this[int index] { get => Chars[index]; set => Chars[index] = value; }
	/// <summary>Marks the current text as saved baseline.</summary>
	public void Save() { lastsavedtext = LineText; IsUnsaved = false; }
	/// <summary>Deep-clones this line including chars and wrapped lines.</summary>
	public Line Clone()
	{
		var copy = new Line(Language) { LineNumber = LineNumber, LineText = LineText, IsUnsaved = IsUnsaved, Folding = Folding, FoldingStartMarker = FoldingStartMarker, FoldingEndMarker = FoldingEndMarker };
		copy.Chars = Chars?.Select(c => new Char(c.C) { T = c.T }).ToList() ?? [];
		copy.WrappedLines = WrappedLines?.Select(w => w.Select(ch => new Char(ch.C) { T = ch.T }).ToList()).ToList() ?? [];
		return copy;
	}
	/// <summary>Sets line text and runs the tokenizer to produce highlighted chars.</summary>
	/// <param name="value">The new line text.</param>
	public void SetLineText(string value) { LineText = value; Chars = FormattedText(value); }
	/// <summary>Sets line text with monochrome chars (fast path, no tokenizer).</summary>
	/// <param name="value">The new line text.</param>
	public void SetLineTextRaw(string value) { LineText = value; Chars = value.Select(x => new Char(x)).ToList(); }
	/// <summary>Runs the tokenizer on the current text and updates chars.</summary>
	public void Tokenize() { Chars = FormattedText(LineText); }
	/// <summary>Appends text and re-tokenizes.</summary>
	/// <param name="value">Text to append.</param>
	public void AddToLineText(string value) { LineText += value; Chars = FormattedText(LineText); }
	public void Add(Char item) => Chars.Add(item);
	public virtual void AddRange(IEnumerable<Char> collection) { }
	public void Clear() => Chars.Clear();
	public bool Contains(Char item) => Chars.Contains(item);
	public void CopyTo(Char[] array, int arrayIndex) => Chars.CopyTo(array, arrayIndex);
	public IEnumerator<Char> GetEnumerator() => Chars.GetEnumerator();
	public int IndexOf(Char item) => Chars.IndexOf(item);
	public void Insert(int index, Char item) => Chars.Insert(index, item);
	public bool Remove(Char item) => Chars.Remove(item);
	public void RemoveAt(int index) => Chars.RemoveAt(index);
	public override string ToString() => LineText;
	internal int GetWordWrapStringFinishPosition(int v, Line line) => 0;
	internal int GetWordWrapStringIndex(int iChar) => 0;
	internal int GetWordWrapStringStartPosition(object v) => 0;
	/// <summary>Tokenizes <paramref name="text"/> into a list of chars with syntax tokens applied.</summary>
	/// <param name="text">The source text to tokenize.</param>
	/// <returns>A list of tokenized chars.</returns>
	public List<Char> FormattedText(string text)
	{
		var groups = text.Select(x => new Char(x)).ToList();
		if (Language.RegexTokens != null)
			foreach (var token in Language.RegexTokens)
				foreach (Match match in Regex.Matches(text, token.Value))
					for (int i = match.Index; i < match.Index + match.Length; i++)
						groups[i].T = token.Key;
		if (Language.WordTokens != null)
			foreach (var token in Language.WordTokens)
			{
				var list = token.Value.Select(w => w.Replace(@"\", @"\\")).ToList();
				string pattern = Language.Name == "ConTeXt"
					? string.Join(@"\b|", list) + @"\b"
					: @"\b" + string.Join(@"\b|\b", list) + @"\b";
				foreach (Match match in Regex.Matches(text, pattern))
					for (int i = match.Index; i < match.Index + match.Length; i++)
						groups[i].T = token.Key;
			}
		if (!string.IsNullOrEmpty(Language.LineComment))
		{
			int ci = text.IndexOf(Language.LineComment);
			if (ci > -1 && !(ci > 0 && Language.EscapeSymbols.Contains(groups[ci - 1].C)))
				for (int i = ci; i < text.Length; i++)
					groups[i].T = Token.Comment;
		}
		return groups;
	}
	private bool FoldableEnd(string text)
	{
		if (Language.FoldingPairs != null)
			foreach (var sf in Language.FoldingPairs)
				if (Regex.Match(text, sf.RegexEnd).Success) return true;
		return false;
	}
	private bool FoldableStart(string text)
	{
		if (Language.FoldingPairs != null)
			foreach (var sf in Language.FoldingPairs)
				if (Regex.Match(text, sf.RegexStart).Success) return true;
		return false;
	}
}
