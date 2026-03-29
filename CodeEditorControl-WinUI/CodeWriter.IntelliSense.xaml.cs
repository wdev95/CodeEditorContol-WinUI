using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeEditorControl_WinUI;

public partial class CodeWriter : UserControl, INotifyPropertyChanged
{
 class CommandAtPosition
 {
  public IntelliSense Command { get; set; }
  public Range CommandRange { get; set; }
  public List<Range> ArgumentsRanges { get; set; }
 }

 private List<Suggestion> Commands
 {
  get => Get(new List<Suggestion>
  {
	new IntelliSense(@"\foo") { IntelliSenseType = IntelliSenseType.Command, Token = Token.Command, Description = "" },
	new IntelliSense(@"\bar") { IntelliSenseType = IntelliSenseType.Command, Token = Token.Command, Description = "" },
	new IntelliSense(@"\foobar") { IntelliSenseType = IntelliSenseType.Command, Token = Token.Command, Description = "" },
  });
  set => Set(value);
 }

 private Place SuggestionStart = new();
 private Suggestion SelectedSuggestion { get => Get<Suggestion>(); set => Set(value); }
 private List<Suggestion> AllOptions { get => Get<List<Suggestion>>(); set => Set(value); }
 private List<Suggestion> AllSuggestions { get => Get(Commands); set => Set(value); }
 private List<Suggestion> Suggestions { get => Get(Commands); set => Set(value); }
 private List<Parameter> Options { get => Get(new List<Parameter>()); set => Set(value); }
 private int SuggestionIndex
 {
  get => Get(-1);
  set
  {
	Set(value);
	if (value == -1) SelectedSuggestion = null;
	else if (Suggestions?.Count > value) { SelectedSuggestion = Suggestions[value]; Lbx_Suggestions.ScrollIntoView(SelectedSuggestion); }
  }
 }

 /// <summary>Updates the suggestion list from the current language.</summary>
 public void UpdateSuggestions()
 {
  AllSuggestions = Language.Commands;
  Suggestions = Language.Commands;
 }

 private void InsertSuggestion()
 {
  TextControl.Focus(FocusState.Keyboard);
  Lines[CursorPlace.iLine].SetLineText(Lines[CursorPlace.iLine].LineText.Remove(SuggestionStart.iChar, CursorPlace.iChar - SuggestionStart.iChar));
  EditActionHistory.Remove(EditActionHistory.LastOrDefault());
  var s = Suggestions[SuggestionIndex];
  TextAction_Paste(s.Name + s.Snippet + s.Options, SuggestionStart, false);
  int iCharStart, iCharEnd;
  if (s.IntelliSenseType == IntelliSenseType.Argument)
  {
	iCharStart = SuggestionStart.iChar + s.Name.Length + s.Snippet.Length;
	iCharEnd = iCharStart + s.Options.Length;
  }
  else { iCharStart = SuggestionStart.iChar + s.Name.Length; iCharEnd = iCharStart; }
  Selection = new(new Place(iCharStart, CursorPlace.iLine), new Place(iCharEnd, CursorPlace.iLine));
  IsSuggesting = false;
 }

 private void FilterSuggestions(int offset = 0)
 {
  if (!IsSuggesting) return;
  try
  {
	string searchString = Lines[SuggestionStart.iLine].LineText.Substring(SuggestionStart.iChar, CursorPlace.iChar - SuggestionStart.iChar);
	var source = IsSuggestingOptions ? AllOptions : AllSuggestions;
	var matching = source.Where(m => m.Name.Contains(searchString)).OrderBy(m => m.Name).ToList();
	if (matching.Count > 0) { Suggestions = matching; SuggestionIndex = 0; }
	else SuggestionIndex = -1;
  }
  catch { }
 }

 private CommandAtPosition GetCommandAtPosition(Place place)
 {
  var result = new CommandAtPosition();
  var matches = Regex.Matches(Lines[place.iLine].LineText, @"(\\.+?\b)(\[(?>\[(?<c>)|[^\[\]]+|\](?<-c>))*(?(c)(?!))\])*");
  if (matches.Any() && AllSuggestions != null)
  {
	foreach (Match cmd in matches)
	{
	 if (!cmd.Success || place.iChar < cmd.Index || place.iChar > cmd.Index + cmd.Length) continue;
	 var name = cmd.Groups[1];
	 result.CommandRange = new(new(name.Index, place.iLine), new(cmd.Index + cmd.Length, place.iLine));
	 result.Command = AllSuggestions.FirstOrDefault(x => x.Name == name.Value) as IntelliSense;
	 if (cmd.Groups.Count > 2)
	 {
	  result.ArgumentsRanges = [];
	  for (int g = 2; g < cmd.Groups.Count; g++)
	  {
		var arg = cmd.Groups[g];
		result.ArgumentsRanges.Add(new(new(arg.Index, place.iLine), new(arg.Index + arg.Length, place.iLine)));
	  }
	 }
	 return result;
	}
  }
  return result;
 }

 private IntelliSense GetCommandFromPlace(Place place)
 {
  string command = "";
  foreach (Match match in Regex.Matches(Lines[place.iLine].LineText, @"(\\.+?)(\s*?)(\[)"))
	command = match?.Groups[1]?.Value;
  return !string.IsNullOrEmpty(command) ? AllSuggestions.FirstOrDefault(x => x.Name == command) as IntelliSense : null;
 }

 private bool IsInsideBrackets(Place place)
 {
  List<BracketPair> pairs = [];
  for (int i = 0; i < Lines[place.iLine].LineText.Length; i++)
  {
	if (Lines[place.iLine].LineText[i] == '[')
	 pairs.Add(new BracketPair(new Place(i, place.iLine), new Place(findClosingParen(Lines[place.iLine].LineText.ToCharArray(), i), place.iLine)));
  }
  return pairs.Any(x => x.iClose >= place && x.iOpen < place);
 }

 private int findClosingParen(char[] text, int openPos)
 {
  int closePos = openPos, counter = 1;
  while (counter > 0)
  {
	if (closePos == text.Length - 1) return ++closePos;
	char c = text[++closePos];
	if (c == '[') counter++;
	else if (c == ']') counter--;
  }
  return closePos;
 }
}