using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;

namespace CodeEditorControl_WinUI;

public partial class CodeWriter : UserControl, INotifyPropertyChanged
{
 public bool CanUndo { get => Get(false); set => Set(value); }
 public bool CanRedo { get => Get(false); set => Set(value); }
 public bool CanToggleComment { get => Get(false); set => Set(value); }
 public ObservableCollection<Place> CursorPlaceHistory = new();
 public ObservableCollection<EditAction> RedoActionHistory = new();
 public ObservableCollection<EditAction> InvertedEditActionHistory { get => Get(new ObservableCollection<EditAction>()); set => Set(value); }
 public ObservableCollection<EditAction> EditActionHistory
 {
  get => (ObservableCollection<EditAction>)GetValue(EditActionHistoryProperty);
  set => SetValue(EditActionHistoryProperty, value);
 }
 private bool _suppressClearRedoOnNextAdd;

 [System.Diagnostics.Conditional("DEBUG")]
 private void LogDebug(string message) { try { LogHelper.CWLog(message); } catch { } }

 private void EditActionHistory_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
 {
  CanUndo = EditActionHistory.Count > 0;
  if (e.Action == NotifyCollectionChangedAction.Add)
  {
	if (!_suppressClearRedoOnNextAdd) RedoActionHistory.Clear();
	_suppressClearRedoOnNextAdd = false;
  }
  CanRedo = RedoActionHistory.Count > 0;
  const int previewCount = 100;
  int start = Math.Max(0, EditActionHistory.Count - previewCount);
  InvertedEditActionHistory = new ObservableCollection<EditAction>(EditActionHistory.Skip(start).Reverse().ToList());
 }

 private void TextAction_Copy()
 {
  if (!IsSelection) return;
  var dp = new DataPackage();
  dp.RequestedOperation = DataPackageOperation.Copy;
  dp.SetText(SelectedText);
  Clipboard.SetContent(dp);
 }

 private bool TryUndoSingleAction(EditAction action)
 {
  try
  {
	if (action == null) return false;
	string involved = action.TextInvolved ?? string.Empty;
	Place start = action.Selection?.VisualStart ?? new Place(0, 0);
	if (action.OldLines != null && action.NewLines != null)
	{
	 int sline = Math.Max(0, Math.Min(action.AffectedStartLine, Lines.Count));
	 for (int i = 0; i < action.NewLines.Count && sline < Lines.Count; i++) Lines.RemoveAt(sline);
	 for (int i = 0; i < action.OldLines.Count; i++)
	 {
	  var nl = new Line(Language) { LineNumber = sline + 1 + i };
       if (action.OldSavedTexts != null && i < action.OldSavedTexts.Count) nl.SavedText = action.OldSavedTexts[i];
	  nl.SetLineText(action.OldLines[i]);
	  Lines.Insert(sline + i, nl);
	 }
       RecalcLineNumbers(sline);
	 Selection = action.Selection ?? new Range(new Place(0, sline));
	 return true;
	}
	if (involved == "\n")
	{
	 if (action.EditActionType is EditActionType.Add or EditActionType.Paste)
	 {
	  if (start.iLine < 0 || start.iLine >= Lines.Count || start.iLine + 1 >= Lines.Count) return false;
	  var cur = Lines[start.iLine];
	  var next = Lines[start.iLine + 1];
	  int indentCount = next.Indents;
	  string tail = indentCount <= next.LineText.Length ? next.LineText.Substring(indentCount) : next.LineText;
	  Lines.RemoveAt(start.iLine + 1);
	  cur.SetLineText(cur.LineText + tail);
	  if (action.SavedTexts is { Count: > 0 }) cur.SavedText = action.SavedTexts[0];
	  RecalcLineNumbers();
	  Selection = new Range(start);
	  return true;
	 }
	 if (action.EditActionType is EditActionType.Delete or EditActionType.Remove)
	 {
	  if (start.iLine < 0 || start.iLine >= Lines.Count) return false;
	  var curLine = Lines[start.iLine];
	  int splitIndex = Math.Max(0, Math.Min(start.iChar, curLine.LineText.Length));
	  string tail = curLine.LineText.Substring(splitIndex);
	  curLine.SetLineText(curLine.LineText.Remove(splitIndex));
	  if (action.SavedTexts is { Count: > 0 }) curLine.SavedText = action.SavedTexts[0];
	  var newline = new Line(Language) { LineNumber = start.iLine + 2 };
	  if (action.SavedTexts is { Count: > 1 }) newline.SavedText = action.SavedTexts[1];
	  newline.SetLineText(tail);
	  Lines.Insert(start.iLine + 1, newline);
	  RecalcLineNumbers();
	  Selection = new Range(new Place(0, start.iLine + 1));
	  return true;
	 }
	}
	if (!string.IsNullOrEmpty(involved) && !involved.Contains('\n'))
	{
	 if (action.EditActionType is EditActionType.Add or EditActionType.Paste)
	 {
	  if (start.iLine < 0 || start.iLine >= Lines.Count) return false;
	  var line = Lines[start.iLine];
	  int removeIndex = Math.Max(0, Math.Min(start.iChar, line.Count));
	  int removeLength = Math.Min(involved.Length, Math.Max(0, line.Count - removeIndex));
	  if (removeLength > 0) line.SetLineText(line.LineText.Remove(removeIndex, removeLength));
	  Selection = new Range(start);
	  return true;
	 }
	 if (action.EditActionType == EditActionType.Delete)
	 {
	  if (start.iLine < 0 || start.iLine >= Lines.Count) return false;
	  var target = Lines[start.iLine];
	  int insertIndex = Math.Max(0, Math.Min(start.iChar, target.LineText.Length));
	  target.SetLineText(target.LineText.Insert(insertIndex, involved));
	  Selection = new Range(new Place(insertIndex + involved.Length, start.iLine));
	  return true;
	 }
	 if (action.EditActionType == EditActionType.Remove)
	 {
	  if (start.iLine < 0 || start.iLine >= Lines.Count) return false;
	  var target = Lines[start.iLine];
	  int insertIndex = Math.Max(0, Math.Min(start.iChar - 1, target.LineText.Length));
	  target.SetLineText(target.LineText.Insert(insertIndex, involved));
	  Selection = new Range(new Place(insertIndex + involved.Length, start.iLine));
	  return true;
	 }
	}
	return false;
  }
  catch { return false; }
 }

 private bool TryRedoSingleAction(EditAction action)
 {
  try
  {
	if (action == null) return false;
	string involved = action.TextInvolved ?? string.Empty;
	Place start = action.Selection?.VisualStart ?? new Place(0, 0);
	if (action.NewLines != null && action.AffectedStartLine >= 0)
	{
	 int sl = Math.Max(0, Math.Min(action.AffectedStartLine, Lines.Count));
	 int oldCount = action.OldLines?.Count ?? 0;
	 for (int i = 0; i < oldCount && sl < Lines.Count; i++) Lines.RemoveAt(sl);
	 for (int i = 0; i < action.NewLines.Count; i++)
	 {
	  var nl = new Line(Language) { LineNumber = sl + 1 + i };
       if (action.NewSavedTexts != null && i < action.NewSavedTexts.Count) nl.SavedText = action.NewSavedTexts[i];
	  nl.SetLineText(action.NewLines[i]);
	  Lines.Insert(sl + i, nl);
	 }
       RecalcLineNumbers(sl);
	 Selection = action.Selection ?? new Range(new Place(0, sl));
	 return true;
	}
	if (involved == "\n")
	{
	 if (action.EditActionType is EditActionType.Add or EditActionType.Paste)
	 {
	  if (start.iLine < 0 || start.iLine >= Lines.Count) return false;
	  var cur = Lines[start.iLine];
	  int splitIndex = Math.Max(0, Math.Min(start.iChar, cur.LineText.Length));
	  string tail = cur.LineText.Substring(splitIndex);
	  cur.SetLineText(cur.LineText.Remove(splitIndex));
	  string indents = string.Concat(Enumerable.Repeat("\t", cur.Indents));
	  var nl = new Line(Language) { LineNumber = start.iLine + 2 };
	  nl.SetLineText(indents + tail);
	  Lines.Insert(start.iLine + 1, nl);
	  RecalcLineNumbers();
	  Selection = new Range(new Place(nl.Indents, start.iLine + 1));
	  return true;
	 }
	 if (action.EditActionType is EditActionType.Delete or EditActionType.Remove)
	 {
	  if (start.iLine < 0 || start.iLine + 1 >= Lines.Count) return false;
	  var cur = Lines[start.iLine];
	  var nextText = Lines[start.iLine + 1].LineText;
	  Lines.RemoveAt(start.iLine + 1);
	  cur.SetLineText(cur.LineText + nextText);
	  RecalcLineNumbers();
	  Selection = new Range(start);
	  return true;
	 }
	}
	if (!string.IsNullOrEmpty(involved) && !involved.Contains('\n'))
	{
	 if (action.EditActionType is EditActionType.Add or EditActionType.Paste)
	 {
	  if (start.iLine < 0 || start.iLine >= Lines.Count) return false;
	  var line = Lines[start.iLine];
	  int insertIndex = Math.Max(0, Math.Min(start.iChar, line.LineText.Length));
	  line.SetLineText(line.LineText.Insert(insertIndex, involved));
	  Selection = new Range(new Place(insertIndex + involved.Length, start.iLine));
	  return true;
	 }
	 if (action.EditActionType == EditActionType.Delete)
	 {
	  if (start.iLine < 0 || start.iLine >= Lines.Count) return false;
	  var target = Lines[start.iLine];
	  int removeIndex = Math.Max(0, Math.Min(start.iChar, target.LineText.Length));
	  int removeLength = Math.Min(involved.Length, Math.Max(0, target.LineText.Length - removeIndex));
	  if (removeLength > 0) target.SetLineText(target.LineText.Remove(removeIndex, removeLength));
	  Selection = new Range(new Place(removeIndex, start.iLine));
	  return true;
	 }
	 if (action.EditActionType == EditActionType.Remove)
	 {
	  if (start.iLine < 0 || start.iLine >= Lines.Count) return false;
	  var target = Lines[start.iLine];
	  int removeIndex = Math.Max(0, start.iChar - 1);
	  if (removeIndex < target.LineText.Length)
	  {
		int removeLength = Math.Min(involved.Length, target.LineText.Length - removeIndex);
		if (removeLength > 0) target.SetLineText(target.LineText.Remove(removeIndex, removeLength));
	  }
	  Selection = new Range(new Place(removeIndex, start.iLine));
	  return true;
	 }
	}
	return false;
  }
  catch { return false; }
 }

 /// <summary>Undoes the specified action, or the last action if <paramref name="action"/> is null.</summary>
 public async Task TextAction_Undo(EditAction action = null)
 {
  try
  {
	if (EditActionHistory.Count == 0) return;
	if (action == null)
	{
	 EditAction last = EditActionHistory.Last();
	 if (TryUndoSingleAction(last))
	 {
	  _suppressClearRedoOnNextAdd = true;
	  EditActionHistory.RemoveAt(EditActionHistory.Count - 1);
	  RedoActionHistory.Add(last);
	  if (IsWrappingEnabled) CalculateLineWraps(0, Math.Max(1, Lines.Count));
	  UpdateText();
	  await DrawText(false, true);
	 }
	 return;
	}
	int index = EditActionHistory.IndexOf(action);
	if (index < 0) return;
	int end = EditActionHistory.Count - 1;
	var undone = new List<EditAction>();
	bool failed = false;
	for (int i = end; i >= index; i--)
	{
	 if (TryUndoSingleAction(EditActionHistory[i])) undone.Add(EditActionHistory[i]);
	 else { failed = true; break; }
	}
	if (!failed)
	{
	 for (int i = end; i >= index; i--)
	 {
	  RedoActionHistory.Add(EditActionHistory[i]);
	  EditActionHistory.RemoveAt(i);
	 }
	 if (IsWrappingEnabled) CalculateLineWraps(0, Math.Max(1, Lines.Count));
	 UpdateText();
	 await DrawText(false, true);
	}
	else
	{
	 foreach (var a in Enumerable.Reverse(undone)) TryRedoSingleAction(a);
	 if (IsWrappingEnabled) CalculateLineWraps(0, Math.Max(1, Lines.Count));
	 UpdateText();
	 await DrawText(false, true);
	}
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
  finally
  {
	CanUndo = EditActionHistory.Count > 0;
	CanRedo = RedoActionHistory.Count > 0;
	checkUnsavedChanges();
  }
 }

 /// <summary>Redoes the last undone action.</summary>
 public async void TextAction_Redo()
 {
  try
  {
	if (RedoActionHistory.Count == 0) return;
	var action = RedoActionHistory.Last();
	if (TryRedoSingleAction(action))
	{
	 _suppressClearRedoOnNextAdd = true;
	 EditActionHistory.Add(action);
	 RedoActionHistory.Remove(action);
	 if (IsWrappingEnabled) CalculateLineWraps(0, Math.Max(1, Lines.Count));
	 UpdateText();
	 await DrawText(false, true);
	}
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
  finally
  {
	CanUndo = EditActionHistory.Count > 0;
	CanRedo = RedoActionHistory.Count > 0;
	checkUnsavedChanges();
  }
 }

 /// <summary>Toggles line comments on the selected lines.</summary>
 public async void TextAction_ToggleComment()
 {
  int tstart = Selection.VisualStart.iLine;
  int tcount = Selection.VisualEnd.iLine - tstart + 1;
  var oldLines = new List<string>();
      var oldSavedTexts = new List<string>();
  for (int li = tstart; li < tstart + tcount && li < Lines.Count; li++) oldLines.Add(Lines[li].LineText);
    for (int li = tstart; li < tstart + tcount && li < Lines.Count; li++) oldSavedTexts.Add(Lines[li].SavedText);
  Place start = Selection.Start, end = Selection.End;
  for (int iline = 0; iline < SelectedLines.Count; iline++)
  {
	var sl = SelectedLines[iline];
	if (sl.LineText.StartsWith(Language.LineComment))
	{
	 sl.SetLineText(sl.LineText.Remove(0, 1));
	 if (iline == 0) start -= 1;
	 if (iline == SelectedLines.Count - 1) end -= 1;
	}
	else if (sl.LineText.StartsWith(string.Concat(Enumerable.Repeat("\t", sl.Indents)) + Language.LineComment))
	{
	 sl.SetLineText(sl.LineText.Remove(sl.Indents, 1));
	 if (iline == 0) start -= 1;
	 if (iline == SelectedLines.Count - 1) end -= 1;
	}
	else
	{
	 sl.SetLineText(sl.LineText.Insert(sl.Indents, Language.LineComment));
	 if (iline == 0) start += 1;
	 if (iline == SelectedLines.Count - 1) end += 1;
	}
  }
  if (IsWrappingEnabled) CalculateLineWraps(CursorPlace.iLine, 1);
  Selection = new(start, end);
  CanvasText.Invalidate();
  UpdateText();
  CanvasScrollbarMarkers.Invalidate();
  LinesChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lines)));
  var newLines = new List<string>();
      var newSavedTexts = new List<string>();
  for (int li = tstart; li < tstart + tcount && li < Lines.Count; li++) newLines.Add(Lines[li].LineText);
    for (int li = tstart; li < tstart + tcount && li < Lines.Count; li++) newSavedTexts.Add(Lines[li].SavedText);
		EditActionHistory.Add(new() { EditActionType = EditActionType.Paste, Selection = Selection, TextInvolved = Language.LineComment, AffectedStartLine = tstart, OldLines = oldLines, NewLines = newLines, OldSavedTexts = oldSavedTexts, NewSavedTexts = newSavedTexts });
 }

 private async void TextAction_Delete(Range selection, bool cut = false)
 {
  try
  {
	if (IsSelection)
	{
	 // Store operation-only action
	 EditActionHistory.Add(new() { EditActionType = EditActionType.Delete, Selection = selection, TextInvolved = SelectedText });

	 if (cut)
	 {
	  TextAction_Copy();
	 }
	 Place startPlace = selection.VisualStart;
	 Place end = selection.VisualEnd;

	 string storetext = "";
	 int removedlines = 0;
	 for (int iLine = startPlace.iLine; iLine <= end.iLine; iLine++)
	 {
	  if (end.iLine == startPlace.iLine)
	  {
		Lines[iLine].SetLineText(Lines[iLine].LineText.Remove(startPlace.iChar, end.iChar - startPlace.iChar));
	  }
	  else if (iLine == startPlace.iLine)
	  {
		if (startPlace.iChar < Lines[iLine].Count)
		 Lines[iLine].SetLineText(Lines[iLine].LineText.Remove(startPlace.iChar));
	  }
	  else if (iLine == end.iLine)
	  {
		if (end.iChar == Lines[iLine - removedlines].Count - 1)
		{
		 Lines.RemoveAt(iLine - removedlines);
		}
		else
		{
		 storetext = Lines[iLine - removedlines].LineText.Substring(end.iChar);
		 Lines.RemoveAt(iLine - removedlines);
		}
	  }
	  else
	  {
		Lines.RemoveAt(iLine - removedlines);
		removedlines += 1;
	  }
	 }
	 if (!string.IsNullOrEmpty(storetext))
	  Lines[startPlace.iLine].AddToLineText(storetext);

	 RecalcLineNumbers(startPlace.iLine);

	 if (IsWrappingEnabled) CalculateLineWraps(Math.Max(0, CursorPlace.iLine - 1), 2);

	 await DrawText(false, true);
	}
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
 }

 private void TextAction_Find()
 {
  IsFindPopupOpen = true;
  if (!SelectedText.Contains("\r\n")) Tbx_Search.Text = SelectedText;
  Tbx_Search.Focus(FocusState.Keyboard);
  Tbx_Search.SelectionStart = Tbx_Search.Text.Length;
 }

 private async void TextAction_Paste(string texttopaste = null, Place placetopaste = null, bool updateposition = true, DragDropModifiers dragDropModifiers = DragDropModifiers.None)
 {
  try
  {
	string text = "";
	if (texttopaste == null)
	{
	 var dpv = Clipboard.GetContent();
	 if (dpv.Contains(StandardDataFormats.Text)) text += await dpv.GetTextAsync();
	}
	else text = texttopaste;
	text = (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
	Place place = placetopaste ?? CursorPlace;
	int pasteStart = place.iLine;
	var pastedlines = text.Split('\n', StringSplitOptions.None);
	var oldLines = new List<string>();
      var oldSavedTexts = new List<string>();
	if (pasteStart >= 0 && pasteStart < Lines.Count) oldLines.Add(Lines[pasteStart].LineText);
      if (pasteStart >= 0 && pasteStart < Lines.Count) oldSavedTexts.Add(Lines[pasteStart].SavedText);
		var action = new EditAction { EditActionType = EditActionType.Paste, Selection = new Range(place), TextInvolved = text, AffectedStartLine = pasteStart, OldLines = oldLines, OldSavedTexts = oldSavedTexts };
	EditActionHistory.Add(action);
	if (IsSelection && place < Selection.VisualStart && dragDropModifiers != DragDropModifiers.Control)
	{
	 TextAction_Delete(Selection);
	 Selection = new(CursorPlace);
	}
	Language lang = Language;
	int i = 0;
	int tabcount = Lines[place.iLine].Indents;
	string stringtomove = "";
	string[] pastedLinesLocal = text.Split('\n', StringSplitOptions.None);
	foreach (string line in pastedLinesLocal)
	{
	 if (i == 0 && pastedlines.Length == 1)
	 {
	  if (place.iChar < Lines[place.iLine].LineText.Length)
		Lines[place.iLine].SetLineText(Lines[place.iLine].LineText.Insert(place.iChar, line));
	  else Lines[place.iLine].SetLineText(Lines[place.iLine].LineText + line);
	 }
	 else if (i == 0)
	 {
	  stringtomove = Lines[place.iLine].LineText.Substring(place.iChar);
	  Lines[place.iLine].SetLineText(Lines[place.iLine].LineText.Remove(place.iChar) + line);
	 }
	 else
	 {
	  var nl = new Line(lang) { LineNumber = place.iLine + 1 + i, IsUnsaved = true };
	  checkUnsavedChanges();
	  nl.SetLineText(string.Concat(Enumerable.Repeat("\t", tabcount)) + line);
	  Lines.Insert(place.iLine + i, nl);
	 }
	 i++;
	}
	if (!string.IsNullOrEmpty(stringtomove)) Lines[place.iLine + i - 1].AddToLineText(stringtomove);
	if (IsSelection && place >= Selection.VisualEnd && dragDropModifiers != DragDropModifiers.Control)
	{
	 TextAction_Delete(Selection);
	 if (place.iLine == Selection.VisualEnd.iLine) Selection = new(Selection.VisualStart);
	}
	if (updateposition)
	{
	 Place end = new(i == 1 ? CursorPlace.iChar + text.Length : pastedlines.Last().Length, place.iLine + i - 1);
	 Selection = new(end);
	 iCharPosition = CursorPlace.iChar;
	}
	var newLines = new List<string>();
     var newSavedTexts = new List<string>();
	int newCount = Math.Max(1, pastedLinesLocal.Length);
	for (int li = pasteStart; li < pasteStart + newCount && li < Lines.Count; li++) newLines.Add(Lines[li].LineText);
      for (int li = pasteStart; li < pasteStart + newCount && li < Lines.Count; li++) newSavedTexts.Add(Lines[li].SavedText);
	action.NewLines = newLines;
    action.NewSavedTexts = newSavedTexts;
	await textChanged();
	await DrawText(false, true);
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
 }

 /// <summary>Adds a <see cref="MenuFlyoutItemBase"/> to the context menu.</summary>
 public void Action_Add(MenuFlyoutItemBase item) => ContextMenu.Items.Add(item);

 /// <summary>Adds a command bar element (currently unused).</summary>
 public void Action_Add(ICommandBarElement item) { }
}