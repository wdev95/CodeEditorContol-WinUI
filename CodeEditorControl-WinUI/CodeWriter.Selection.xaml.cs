using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Windows.Foundation;

namespace CodeEditorControl_WinUI;

public partial class CodeWriter : UserControl, INotifyPropertyChanged
{
 private Point previousPosition;
 private PointerPoint CurrentPointer { get; set; }

 /// <summary>Selects the entire content of the specified line.</summary>
 public void SelectLine(Place start) => Selection = new(new(0, start.iLine), new(Lines[start.iLine].Count, start.iLine));

 /// <summary>Selects all text in the editor.</summary>
 public void TextAction_SelectText(Range range = null)
 {
  if (range == null && Lines.Count > 0) Selection = new Range(new(0, 0), new(Lines.Last().Count, Lines.Count - 1));
 }

 private async void TextControl_PointerMoved(object sender, PointerRoutedEventArgs e)
 {
  try
  {
	CurrentPointerPoint = e.GetCurrentPoint(TextControl);
	if (isDragging) return;
	if (e.Pointer.PointerDeviceType is not (PointerDeviceType.Mouse or PointerDeviceType.Pen)) return;
	Place place = await PointToPlace(CurrentPointerPoint.Position);
	if (isSelecting && CurrentPointerPoint.Properties.IsLeftButtonPressed)
	{
	 if (!isLineSelect) Selection = new Range(Selection.Start, await PointToPlace(CurrentPointerPoint.Position));
	 else
	 {
	  place.iChar = Lines[place.iLine].Count;
	  Selection = new Range(new(0, Selection.Start.iLine), place);
	 }
	}
	else if (isMiddleClickScrolling) middleClickScrollingEndPoint = CurrentPointerPoint.Position;
	else
	{
	 if (CurrentPointerPoint.Position.X < Width_Left) ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
	 else if (IsSelection)
	 {
	  var vs = Selection.VisualStart; var ve = Selection.VisualEnd;
	  ProtectedCursor = place < vs || place >= ve
		? InputSystemCursor.Create(InputSystemCursorShape.IBeam)
		: InputSystemCursor.Create(place.iChar < Lines[place.iLine].Count ? InputSystemCursorShape.Arrow : InputSystemCursorShape.IBeam);
	 }
	 else ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.IBeam);
	}
	e.Handled = true;
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
 }

 private async void TextControl_PointerPressed(object sender, PointerRoutedEventArgs e)
 {
  IsSuggesting = false;
  Focus(FocusState.Pointer);
  var cp = e.GetCurrentPoint(TextControl);
  try
  {
	if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
	{
	 Place start = await PointToPlace(cp.Position);
	 Place end = new(start.iChar, start.iLine);
	 foreach (Match m in Regex.Matches(Lines[start.iLine].LineText, @"\b\w+?\b"))
	 {
	  if (start.iChar <= m.Index + m.Length && start.iChar >= m.Index) { start.iChar = m.Index; end.iChar = m.Index + m.Length; }
	 }
	 Selection = new(start, end);
	}
	else if (e.Pointer.PointerDeviceType is PointerDeviceType.Mouse or PointerDeviceType.Pen)
	{
	 if (cp.Properties.IsLeftButtonPressed)
	 {
	  if (IsSelection)
	  {
		Place pos = await PointToPlace(cp.Position);
		if (pos > Selection.VisualStart && pos <= Selection.VisualEnd) { isDragging = true; draggedText = SelectedText; draggedSelection = new(Selection); return; }
	  }
	  isLineSelect = cp.Position.X < Width_Left;
	  isSelecting = true;
	  if (!isLineSelect)
	  {
		if (previousPosition != cp.Position)
		{
		 Place start = await PointToPlace(cp.Position);
		 Selection = new(start, start);
		 if (CursorPlaceHistory.Count == 0 || start.iLine != CursorPlaceHistory.Last().iLine) CursorPlaceHistory.Add(start);
		}
		else
		{
		 Place start = await PointToPlace(previousPosition);
		 Place end = new(start.iChar, start.iLine);
		 foreach (Match m in Regex.Matches(Lines[start.iLine].LineText, string.Join('|', Language.WordSelectionDefinitions)))
		 {
		  if (start.iChar <= m.Index + m.Length && start.iChar >= m.Index) { start.iChar = m.Index; end.iChar = m.Index + m.Length; }
		 }
		 Selection = new(start, end);
		 DoubleClicked?.Invoke(this, new());
		}
		previousPosition = cp.Position;
	  }
	  else { SelectLine(await PointToPlace(cp.Position)); }
	  isMiddleClickScrolling = false;
	  previousPosition = cp.Position;
	  iCharPosition = CursorPlace.iChar;
	 }
	 else if (cp.Properties.IsRightButtonPressed)
	 {
	  Place rp = await PointToPlace(cp.Position);
	  if (IsSelection) { if (rp <= Selection.VisualStart || rp >= Selection.VisualEnd) Selection = new Range(rp); }
	  else Selection = new Range(rp);
	  isMiddleClickScrolling = false;
	 }
	 else if (cp.Properties.IsXButton1Pressed)
	 {
	  if (CursorPlaceHistory.Count > 1) { Selection = new Range(CursorPlaceHistory[^2]); CursorPlaceHistory.Remove(CursorPlaceHistory.Last()); }
	 }
	 else if (cp.Properties.IsMiddleButtonPressed)
	 {
	  if (!isMiddleClickScrolling)
	  {
		void StartMiddleScroll(InputSystemCursorShape shape, Action<DispatcherTimer> onTick)
		{
		 isMiddleClickScrolling = true;
		 ProtectedCursor = InputSystemCursor.Create(shape);
		 middleClickScrollingStartPoint = cp.Position;
		 var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
		 t.Tick += (a, b) => { if (!isMiddleClickScrolling) { ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.IBeam); ((DispatcherTimer)a).Stop(); } else onTick(t); };
		 t.Start();
		}
		bool vScroll = VerticalScroll.Maximum + Scroll.ActualHeight > Scroll.ActualHeight;
		bool hScroll = HorizontalScroll.Maximum + Scroll.ActualWidth > Scroll.ActualWidth;
		if (vScroll && hScroll)
		 StartMiddleScroll(InputSystemCursorShape.SizeAll, _ => { VerticalScroll.Value += middleClickScrollingEndPoint.Y - middleClickScrollingStartPoint.Y; HorizontalScroll.Value += middleClickScrollingEndPoint.X - middleClickScrollingStartPoint.X; });
		else if (vScroll)
		 StartMiddleScroll(InputSystemCursorShape.SizeNorthSouth, _ => VerticalScroll.Value += middleClickScrollingEndPoint.Y - middleClickScrollingStartPoint.Y);
		else if (hScroll)
		 StartMiddleScroll(InputSystemCursorShape.SizeWestEast, _ => HorizontalScroll.Value += middleClickScrollingEndPoint.X - middleClickScrollingStartPoint.X);
	  }
	  else isMiddleClickScrolling = false;
	 }
	}
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
  e.Handled = true;
 }

 private async void TextControl_PointerReleased(object sender, PointerRoutedEventArgs e)
 {
  try
  {
	var cp = e.GetCurrentPoint(TextControl);
	Place place = await PointToPlace(cp.Position);
	isLineSelect = false;
	if (isSelecting) { isSelecting = false; e.Handled = true; TextControl.Focus(FocusState.Pointer); }
	if (isDragging && place > Selection.VisualStart && place <= Selection.VisualEnd)
	{
	 e.Handled = true;
	 Selection = new Range(place);
	 ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.IBeam);
	 Focus(FocusState.Keyboard);
	}
	else if (isDragging) Focus(FocusState.Pointer);
	isDragging = false;
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
 }
}