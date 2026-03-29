using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Windows.System;

namespace CodeEditorControl_WinUI;

public partial class CodeWriter : UserControl, INotifyPropertyChanged
{
 private int searchindex;
 public bool IsFindPopupOpen { get => Get(false); set { Set(value); if (!value) { Tbx_Search.Text = ""; TextControl.Focus(FocusState.Keyboard); } } }
 public bool IsMatchCase { get => Get(false); set { Set(value); Tbx_SearchChanged(null, null); } }
 public bool IsRegex { get => Get(false); set { Set(value); Tbx_SearchChanged(null, null); } }
 public List<SearchMatch> SearchMatches { get => Get(new List<SearchMatch>()); set { Set(value); CanvasScrollbarMarkers.Invalidate(); } }

 Microsoft.UI.Dispatching.DispatcherQueue _queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

 private void Btn_SearchClose(object sender, RoutedEventArgs e) => IsFindPopupOpen = false;

 private void Btn_SearchNext(object sender, RoutedEventArgs e)
 {
  try
  {
	searchindex++;
	if (searchindex >= SearchMatches.Count) searchindex = 0;
	if (SearchMatches.Count > 0)
	{
	 var sm = SearchMatches[searchindex];
	 Selection = new(new Place(sm.iChar, sm.iLine));
	}
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
 }

 private async void Btn_ReplaceNext(object sender, RoutedEventArgs e)
 {
  try
  {
	if (SearchMatches.Count <= 0) return;
	var sm = SearchMatches[searchindex];
	Lines[sm.iLine].SetLineText(Lines[sm.iLine].LineText.Remove(sm.iChar, sm.Match.Length).Insert(sm.iChar, Tbx_Replace.Text));
	EditActionHistory.Add(new() { EditActionType = EditActionType.Paste, Selection = Selection, TextInvolved = Tbx_Replace.Text });
	SearchMatches.RemoveAt(searchindex);
	UpdateText();
	await DrawText(false, true);
	Selection = new(new(sm.iChar, sm.iLine), new(sm.iChar + Tbx_Replace.Text.Length, sm.iLine));
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
 }

 private async void Btn_ReplaceAll(object sender, RoutedEventArgs e)
 {
  try
  {
	if (SearchMatches.Count <= 0) return;
	foreach (var sm in SearchMatches)
	 Lines[sm.iLine].SetLineText(Lines[sm.iLine].LineText.Remove(sm.iChar, sm.Match.Length).Insert(sm.iChar, Tbx_Replace.Text));
	EditActionHistory.Add(new() { EditActionType = EditActionType.Paste, Selection = Selection, TextInvolved = "< multiple replacements >" });
	Selection = new(new(SearchMatches.Last().iChar, SearchMatches.Last().iLine), new(SearchMatches.Last().iChar + Tbx_Replace.Text.Length, SearchMatches.Last().iLine));
	SearchMatches.Clear();
	UpdateText();
	await DrawText(false, true);
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
 }

 private void Tbx_Search_KeyDown(object sender, KeyRoutedEventArgs e)
 {
  switch (e.Key)
  {
	case VirtualKey.Enter: Btn_SearchNext(null, null); e.Handled = true; break;
	case VirtualKey.Escape: IsFindPopupOpen = false; e.Handled = true; break;
	case VirtualKey.Tab: Tbx_Replace.Focus(FocusState.Keyboard); e.Handled = true; break;
	case VirtualKey.Back: e.Handled = true; break;
  }
 }

 private void Tbx_Replace_KeyDown(object sender, KeyRoutedEventArgs e)
 {
  switch (e.Key)
  {
	case VirtualKey.Enter: Btn_ReplaceNext(null, null); break;
	case VirtualKey.Escape: IsFindPopupOpen = false; break;
	case VirtualKey.Tab: Tbx_Search.Focus(FocusState.Keyboard); break;
	case VirtualKey.Back: break;
  }
  e.Handled = true;
 }

 private void Tbx_SearchChanged(object sender, TextChangedEventArgs e)
 {
  try
  {
	searchindex = 0;
	string text = Tbx_Search.Text;
	_queue.TryEnqueue(() =>
	{
	 if (text == "") { SearchMatches.Clear(); CanvasScrollbarMarkers.Invalidate(); CanvasSelection.Invalidate(); return; }
	 SearchMatches.Clear();
	 for (int iLine = 0; iLine < Lines.Count; iLine++)
	 {
	  if (IsRegex)
	  {
		MatchCollection coll = null;
		try { coll = Regex.Matches(Lines[iLine].LineText, text, IsMatchCase ? RegexOptions.None : RegexOptions.IgnoreCase); } catch { }
		if (coll != null) foreach (Match m in coll) SearchMatches.Add(new() { Match = m.Value, iChar = m.Index, iLine = iLine });
	  }
	  else
	  {
		int next = 0;
		while (next != -1)
		{
		 next = Lines[iLine].LineText.IndexOf(text, next, IsMatchCase ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase);
		 if (next != -1) { SearchMatches.Add(new() { Match = text, iChar = next, iLine = iLine }); next++; }
		}
	  }
	 }
	 if (SearchMatches.Count > 0) Selection = new Range(new Place(SearchMatches[0].iChar, SearchMatches[0].iLine));
	 CanvasScrollbarMarkers.Invalidate();
	 CanvasSelection.Invalidate();
	});
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
 }
}