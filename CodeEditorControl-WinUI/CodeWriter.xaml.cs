using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace CodeEditorControl_WinUI;

public partial class CodeWriter : UserControl, INotifyPropertyChanged
{
 public static CodeWriter Current;
 public bool IsInitialized;
 public List<Line> SelectedLines = new();
 private new ElementTheme ActualTheme = ElementTheme.Dark;
 private CoreVirtualKeyStates controlKeyState = CoreVirtualKeyStates.None;
 private bool dragStarted;
 private int iCharOffset;
 private int iCharPosition;
 private bool invoked;
 private bool IsSettingValue;
 private int maxchars;
 private CoreVirtualKeyStates shiftKeyState = CoreVirtualKeyStates.None;
 private bool tempFocus;
 private readonly object _textUpdateLock = new();
 private bool _isLoadingLines;
 private bool _textUpdateRunning;
 private bool _pendingTextUpdate;
 private bool _deferDrawDuringLoad = true;
 private bool _deferredDrawRequested;
 private bool _startNewAddActionOnNextChar;

 public CodeWriter()
 {
  InitializeComponent();
  Command_Copy = new RelayCommand(() => TextAction_Copy());
  Command_Paste = new RelayCommand(() => TextAction_Paste());
  Command_Delete = new RelayCommand(() => TextAction_Delete(Selection));
  Command_Cut = new RelayCommand(() => { TextAction_Delete(Selection, true); Selection = new(Selection.VisualStart); });
  Command_SelectAll = new RelayCommand(() => TextAction_SelectText());
  Command_Find = new RelayCommand(() => TextAction_Find());
  Command_Undo = new RelayCommand(() => TextAction_Undo());
  Command_ToggleComment = new RelayCommand(() => TextAction_ToggleComment());
  Current = this;
  TextChangedTimer.Tick += async (s, e) => await TextChangedTimer_TickAsync();
 }

 public event PropertyChangedEventHandler CursorPlaceChanged;
 public event EventHandler DoubleClicked;
 public event ErrorEventHandler ErrorOccured;
 public event EventHandler<string> InfoMessage;
 public event EventHandler Initialized;
 public event PropertyChangedEventHandler LinesChanged;
 public event PropertyChangedEventHandler TextChanged;

 public Place CursorPlace
 {
  get => Get(new Place(0, 0));
  set
  {
	Set(value);
	if (isCanvasLoaded && !isSelecting)
	{
	 if (!isLineSelect)
	 {
	  var width = Scroll.ActualWidth - Width_Left;
	  if (value.iChar * CharWidth < HorizontalScroll.Value)
		HorizontalScroll.Value = value.iChar * CharWidth;
	  else if ((value.iChar + 5) * CharWidth - width - HorizontalScroll.Value > 0)
		HorizontalScroll.Value = Math.Max((value.iChar + 5) * CharWidth - width, 0);
	 }
	 if ((value.iLine + 1) * CharHeight <= VerticalScroll.Value)
	  VerticalScroll.Value = value.iLine * CharHeight;
	 else if ((value.iLine + 2) * CharHeight > VerticalScroll.Value + Scroll.ActualHeight)
	  VerticalScroll.Value = Math.Min((value.iLine + 2) * CharHeight - Scroll.ActualHeight, VerticalScroll.Maximum);
	}
	Point point;
	try { point = IsWrappingEnabled ? PlaceToPoint(value) : new Point(Width_Left + HorizontalOffset + value.iChar * CharWidth, (value.iLine - (VisibleLines.Count > 0 ? VisibleLines[0].LineNumber : 0)) * CharHeight); }
	catch { point = new Point(Width_Left + HorizontalOffset + value.iChar * CharWidth, (value.iLine - (VisibleLines.Count > 0 ? VisibleLines[0].LineNumber : 0)) * CharHeight); }
	CursorPoint = new Point(point.X, point.Y + CharHeight);
	IsSettingValue = true;
	CurrentLine = value;
	IsSettingValue = false;
	CanvasBeam.Invalidate();
	CanvasScrollbarMarkers.Invalidate();
  }
 }

 public Point CursorPoint { get => Get(new Point()); set => Set(value); }
 public bool IsSelection { get => Get(false); set => Set(value); }
 public bool IsSuggesting
 {
  get => Get(false);
  set { Set(value); if (value) SuggestionIndex = -1; }
 }

 public bool IsSuggestingOptions { get => Get(false); set => Set(value); }
 public List<Line> Lines { get => Get(new List<Line>()); set => Set(value); }
 public string SelectedText
 {
  get
  {
	string text = "";
	if (Selection.Start == Selection.End)
	 return "";

	Place start = Selection.VisualStart;
	Place end = Selection.VisualEnd;

	if (start.iLine == end.iLine)
	{
	 text = Lines[start.iLine].LineText.Substring(start.iChar, end.iChar - start.iChar);
	}
	else
	{
	 for (int iLine = start.iLine; iLine <= end.iLine; iLine++)
	 {
	  if (iLine == start.iLine)
		text += Lines[iLine].LineText.Substring(start.iChar) + "\r\n";
	  else if (iLine == end.iLine)
		text += Lines[iLine].LineText.Substring(0, end.iChar);
	  else
		text += Lines[iLine].LineText + "\r\n";
	 }
	}

	return text;
  }
 }

 public Range Selection
 {
  get => Get(new Range(CursorPlace, CursorPlace));
  set
  {
	Set(value);
	CursorPlace = new Place(value.End.iChar, value.End.iLine);
	IsSelection = value.Start != value.End;
	var linesSnapshot = SnapshotSafe(Lines);
	SelectedLines = linesSnapshot.Where(x => x != null && x.iLine >= value.VisualStart.iLine && x.iLine <= value.VisualEnd.iLine).ToList();
	CanvasSelection.Invalidate();
  }
 }

 public List<SyntaxError> SyntaxErrors
 {
  get => Get(new List<SyntaxError>()); set
  {
	Set(value);
	DispatcherQueue.TryEnqueue(() => { CanvasScrollbarMarkers.Invalidate(); CanvasText.Invalidate(); });
  }
 }

 public List<Line> VisibleLines { get; set; } = new();
 private int VisibleStartSubline { get; set; }
 private PointerPoint CurrentPointerPoint { get; set; }
 private bool isCanvasLoaded => CanvasText.IsLoaded;
 private bool IsFocused { get => Get(false); set => Set(value); }
 private bool isLineSelect { get; set; }
 private bool isMiddleClickScrolling { get => Get(false); set => Set(value); }
 private bool isSelecting { get; set; }
 private int iVisibleChars => (int)(((int)TextControl.ActualWidth - Width_Left) / CharWidth);
 private int iVisibleLines => (int)(((int)TextControl.ActualHeight) / CharHeight);
 private DispatcherTimer TextChangedTimer { get; set; } = new() { Interval = TimeSpan.FromMilliseconds(200) };
 private string TextChangedTimerLastText { get; set; } = "";
 private CanvasTextFormat TextFormat { get; set; }

 public Char this[Place place]
 {
  get => Lines[place.iLine][place.iChar];
  set => Lines[place.iLine][place.iChar] = value;
 }

 public Line this[int iLine] { get => Lines[iLine]; }

 public static int IntLength(int i) => i <= 0 ? 1 : (int)Math.Floor(Math.Log10(i)) + 1;

 /// <summary>Forces a redraw of the text canvas.</summary>
 public void RedrawText()
 {
  try { CanvasText.Invalidate(); }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
 }

 /// <summary>Saves all lines and resets unsaved state.</summary>
 public async Task Save()
 {
  await Task.Run(() => { foreach (Line line in new List<Line>(Lines)) line.Save(); SyntaxErrors.Clear(); });
  IsSuggesting = false;
  IsSuggestingOptions = false;
  HasUnsavedChanges = false;
  await ForceUpdateTextAsync();
  CanvasText.Invalidate();
  CanvasScrollbarMarkers.Invalidate();
 }

 private void CalculateLineWraps(int startLine, int count)
 {
  if (!IsWrappingEnabled || Lines == null || Lines.Count == 0) return;
  lock (Lines)
  {
	int s = Math.Max(0, startLine);
	int e = Math.Min(Lines.Count - 1, startLine + Math.Max(1, count) - 1);
	for (int li = s; li <= e; li++)
	{
	 var line = Lines[li];
	 if (line == null) continue;
	 var wrappedLines = new List<List<Char>>();
	 if (line.Count == 0) { wrappedLines.Add(new List<Char>()); line.WrappedLines = wrappedLines; continue; }
	 List<Char> current = new();
	 int currentWidth = 0;
	 for (int ci = 0; ci < line.Count; ci++)
	 {
	  Char c = line[ci];
	  int charWidth = c.C == '\t' ? TabLength : 1;
	  if (currentWidth + charWidth > Math.Max(1, iVisibleChars) && current.Count > 0)
	  { wrappedLines.Add(current); current = new(); currentWidth = 0; }
	  current.Add(c);
	  currentWidth += charWidth;
	  if (currentWidth > Math.Max(1, iVisibleChars) && current.Count > 0)
	  { wrappedLines.Add(current); current = new(); currentWidth = 0; }
	 }
	 if (current.Count > 0) wrappedLines.Add(current);
	 if (wrappedLines.Count == 0) wrappedLines.Add(new List<Char>());
	 line.WrappedLines = wrappedLines;
	}
  }
 }

 private (int startLine, List<string> oldLines, List<string> newLines) CreateLineDeltas(int startLine, int count, Func<int, string> newLineProvider = null)
 {
  var oldLines = new List<string>();
  var newLines = new List<string>();
  int s = Math.Max(0, startLine);
  int e = Math.Min(Lines?.Count - 1 ?? -1, startLine + Math.Max(1, count) - 1);
  for (int i = s; i <= e; i++)
  {
   oldLines.Add(Lines[i]?.LineText ?? string.Empty);
   if (newLineProvider != null)
    newLines.Add(newLineProvider(i));
   else
    newLines.Add(oldLines.Last());
  }
  return (s, oldLines, newLines);
 }

 private void CanvasBeam_Draw(CanvasControl sender, CanvasDrawEventArgs args)
 {
  try
  {
	// Defensive snapshots of visible lines to avoid concurrent modification issues
	var visibleSnapshot = SnapshotSafe(VisibleLines).ToList();
	if (visibleSnapshot.Count > 0)
	{
	 int globalYOffset = -VisibleStartSubline * CharHeight;

	 int x = (int)(Width_Left + HorizontalOffset + CursorPlace.iChar * CharWidth);
	 int y = (int)(globalYOffset + (CursorPlace.iLine - visibleSnapshot[0].LineNumber + 1) * CharHeight - 1 / 2 * CharHeight);

	 // Guard access to Lines using a safe snapshot where needed
	 var linesSnapshot = SnapshotSafe(Lines);
	 if (CursorPlace.iLine >= 0 && CursorPlace.iLine < linesSnapshot.Length)
	 {
	  for (int i = 0; i < CursorPlace.iChar; i++)
	  {
		var lineRef = linesSnapshot[CursorPlace.iLine];
		if (lineRef != null && lineRef.Count > i)
		 if (lineRef[i].C == '\t')
		 {
		  x += CharWidth * (TabLength - 1);
		 }
	  }
	 }

	 Point point;
	 try
	 {
	  point = PlaceToPoint(CursorPlace);
	 }
	 catch
	 {
	  point = new Point(x, y);
	 }

	 y = (int)point.Y;
	 x = (int)point.X;

	 if (Selection.Start == CursorPlace)
	 {
	  args.DrawingSession.FillRectangle(Width_Left, y, (int)TextControl.ActualWidth - Width_Left, CharHeight, ActualTheme == ElementTheme.Light ? Color_SelelectedLineBackground.InvertColorBrightness() : Color_SelelectedLineBackground);
	 }

	 if (y <= TextControl.ActualHeight && y >= 0 && x <= TextControl.ActualWidth && x >= Width_Left)
	  args.DrawingSession.DrawLine(new Vector2(x, y), new Vector2(x, y + CharHeight), ActualTheme == ElementTheme.Light ? Color_Beam.InvertColorBrightness() : Color_Beam, 2f);

	 int xms = (int)(Width_Left);
	 int iCharStart = iCharOffset;
	 int xme = (int)TextControl.ActualWidth;
	 int iCharEnd = iCharStart + (int)((xme - xms) / CharWidth);

	 if (ShowHorizontalTicks)
	  for (int iChar = iCharStart; iChar < iCharEnd; iChar++)
	  {
		int xs = (int)((iChar - iCharStart) * CharWidth) + xms;
		if (iChar % 10 == 0)
		 args.DrawingSession.DrawLine(xs, 0, xs, CharHeight / 8, new CanvasSolidColorBrush(sender, Color_LineNumber), 2f);
	  }
	}
  }
  catch (Exception ex)
  {
	ErrorOccured?.Invoke(this, new ErrorEventArgs(ex));
  }
 }

 private void CanvasLineInfo_Draw(CanvasControl sender, CanvasDrawEventArgs args)
 {
  try
  {
	sender.DpiScale = XamlRoot.RasterizationScale > 1.0d ? 1.15f : 1.0f;
	args.DrawingSession.Antialiasing = CanvasAntialiasing.Aliased;

	if (VisibleLines.Count == 0 || Lines == null || Lines.Count == 0)
	 return;


	int foldPos = Width_LeftMargin + Width_LineNumber + Width_ErrorMarker + Width_WarningMarker;
	int errorPos = Width_LeftMargin + Width_LineNumber;
	int warningPos = errorPos + Width_ErrorMarker;
	float thickness = Math.Max(1, CharWidth / 6f);

	// Copy foldings under lock to avoid races with updateFoldingPairs
	List<Folding> folds;
	lock (_foldingsLock)
	{
	 folds = foldings?.ToList() ?? new List<Folding>();
	}

	int visibleStartLineNumber = VisibleLines[0].iLine;
	int totalWrapsSoFar = 0;
	int globalYOffset = -VisibleStartSubline * CharHeight;

	// iterate visible logical lines and their wrapped sublines
	foreach (var line in VisibleLines)
	{
	 if (line == null) continue;
	 int iLine = line.iLine;
	 var wrapped = (IsWrappingEnabled && line.WrappedLines != null && line.WrappedLines.Count > 0)
						? line.WrappedLines
						: new List<List<Char>> { LineToCharList(line) };

	 for (int wi = 0; wi < wrapped.Count; wi++)
	 {
	  int y = globalYOffset + CharHeight * (iLine - visibleStartLineNumber + totalWrapsSoFar);
	  // left background for this visual row
	  args.DrawingSession.FillRectangle(0, y, Width_Left - Width_TextIndent, CharHeight, Color_LeftBackground);

	  // Only draw the line number on the first subline
	  if (ShowLineNumbers && wi == 0)
	  {
		args.DrawingSession.DrawText((iLine + 1).ToString(), Width_LineNumber + Width_LeftMargin, y, ActualTheme == ElementTheme.Light ? Color_LineNumber.InvertColorBrightness() : Color_LineNumber, new CanvasTextFormat() { FontFamily = FontUri, FontSize = ScaledFontSize, HorizontalAlignment = CanvasHorizontalAlignment.Right });
	  }

	  // Folding markers: draw at first subline; for wrapped continuation rows draw connector lines when inside a folding range
	  if (IsFoldingEnabled && Language.FoldingPairs != null)
	  {
		if (wi == 0)
		{
		 if (folds != null && folds.Any(f => f.StartLine == iLine))
		 {
		  float w = CharWidth * 0.75f;
		  args.DrawingSession.FillRectangle(foldPos + (CharWidth - w) / 2f, y + CharHeight / 2 - w / 2f, w, w, ActualTheme == ElementTheme.Light ? Color_FoldingMarker.InvertColorBrightness() : Color_FoldingMarker);
		 }
		 else if (folds != null && folds.Any(f => f.Endline == iLine))
		 {
		  args.DrawingSession.DrawLine(foldPos + CharWidth / 2f - thickness / 2f, y + CharHeight / 2f, foldPos + CharWidth, y + CharHeight / 2f, ActualTheme == ElementTheme.Light ? Color_FoldingMarker.InvertColorBrightness() : Color_FoldingMarker, thickness);
		  args.DrawingSession.DrawLine(foldPos + CharWidth / 2f, y, foldPos + CharWidth / 2f, y + CharHeight / 2f, ActualTheme == ElementTheme.Light ? Color_FoldingMarker.InvertColorBrightness() : Color_FoldingMarker, thickness);
		 }

		 // draw vertical connectors for ranges that span this logical line
		 if (folds != null && folds.Any(f => iLine > f.StartLine && iLine < f.Endline))
		 {
		  args.DrawingSession.DrawLine(foldPos + CharWidth / 2f, y - CharHeight / 2f, foldPos + CharWidth / 2f, y + CharHeight * 1.5f, ActualTheme == ElementTheme.Light ? Color_FoldingMarker.InvertColorBrightness() : Color_FoldingMarker, thickness);
		 }
		}
		else
		{
		 // continuation wrapped rows: draw vertical connector if this logical line is inside any fold range (preserve visual continuity)
		 if (folds != null && folds.Any(f => iLine > f.StartLine && iLine <= f.Endline))
		 {
		  args.DrawingSession.DrawLine(foldPos + CharWidth / 2f, y - CharHeight / 2f, foldPos + CharWidth / 2f, y + CharHeight / 2f, ActualTheme == ElementTheme.Light ? Color_FoldingMarker.InvertColorBrightness() : Color_FoldingMarker, thickness);
		 }
		}
	  }

	  // Line markers (unsaved / errors / warnings) should appear on first subline only
	  if (ShowLineMarkers && wi == 0)
	  {
		if (line.IsUnsaved)
		 args.DrawingSession.FillRectangle(warningPos, y, Width_ErrorMarker, CharHeight, ActualTheme == ElementTheme.Light ? Color_UnsavedMarker.ChangeColorBrightness(-0.2f) : Color_UnsavedMarker);

		// Defensive check on SyntaxErrors
		var syntaxErrorsSnapshot = SnapshotSafe(SyntaxErrors);
		if (syntaxErrorsSnapshot.Any(x => x.iLine == iLine))
		{
		 SyntaxError lineError = syntaxErrorsSnapshot.First(x => x.iLine == iLine);
		 if (lineError.SyntaxErrorType == SyntaxErrorType.Error)
		 {
		  args.DrawingSession.FillRectangle(errorPos, y, Width_ErrorMarker, CharHeight, Color.FromArgb(255, 200, 40, 40));
		 }
		 if (lineError.SyntaxErrorType == SyntaxErrorType.Warning)
		 {
		  args.DrawingSession.FillRectangle(warningPos, y, Width_WarningMarker, CharHeight, Color.FromArgb(255, 180, 180, 40));
		 }
		}
	  }

	  // advance for next wrapped visual row
	  if (wi < wrapped.Count - 1)
	  {
		totalWrapsSoFar++;
	  }
	 } // wrapped sublines
	} // visible logical lines
  }
  catch (Exception ex)
  {
	ErrorOccured?.Invoke(this, new ErrorEventArgs(ex));
  }
 }

 private void CanvasScrollbarMarkers_Draw(CanvasControl sender, CanvasDrawEventArgs args)
 {
  try
  {
	if (ShowScrollbarMarkers)
	{
	 float markersize = (float)Math.Max(CharHeight / (VerticalScroll.Maximum + ScrollContent.ActualHeight) * CanvasScrollbarMarkers.ActualHeight, 4f);
	 float width = (float)VerticalScroll.ActualWidth;
	 float height = (float)CanvasScrollbarMarkers.ActualHeight;
	 int linecount = Math.Max(1, Lines?.Count ?? 1);

	 // Defensive snapshots to avoid enumerating collections that may be modified concurrently.
	 var searchMatches = SnapshotSafe(SearchMatches);
	 var linesSnapshot = SnapshotSafe(Lines);
	 var syntaxErrors = SnapshotSafe(SyntaxErrors);

	 foreach (var search in searchMatches)
	 {
	  // guard against invalid line index
	  if (search == null) continue;
	  if (search.iLine < 0 || search.iLine >= linecount) continue;
	  args.DrawingSession.DrawLine(width / 3f, search.iLine / (float)linecount * height, width * 2 / 3f, search.iLine / (float)linecount * height, ActualTheme == ElementTheme.Light ? Colors.LightGray.ChangeColorBrightness(-0.3f) : Colors.LightGray, markersize);
	 }

	 if (linesSnapshot.Length > 0)
	 {
	  foreach (var line in linesSnapshot)
	  {
		if (line == null) continue;
		if (!line.IsUnsaved) continue;
		if (line.iLine < 0 || line.iLine >= linecount) continue;
		args.DrawingSession.DrawLine(0, line.iLine / (float)linecount * height, width * 1 / 3f, line.iLine / (float)linecount * height, ActualTheme == ElementTheme.Light ? Color_UnsavedMarker.ChangeColorBrightness(-0.2f) : Color_UnsavedMarker, markersize);
	  }
	 }

	 foreach (var error in syntaxErrors)
	 {
	  if (error == null) continue;
	  if (error.iLine < 0 || error.iLine >= linecount) continue;

	  if (error.SyntaxErrorType == SyntaxErrorType.Error)
	  {
		args.DrawingSession.DrawLine(width * 2 / 3f, error.iLine / (float)linecount * height, width, error.iLine / (float)linecount * height, ActualTheme == ElementTheme.Light ? Colors.Red.ChangeColorBrightness(-0.2f) : Colors.Red, markersize);
	  }
	  else if (error.SyntaxErrorType == SyntaxErrorType.Warning)
	  {
		args.DrawingSession.DrawLine(width * 2 / 3f, error.iLine / (float)linecount * height, width, error.iLine / (float)linecount * height, ActualTheme == ElementTheme.Light ? Colors.Yellow.ChangeColorBrightness(-0.2f) : Colors.Yellow, markersize);
	  }
	 }

	 // Clamp cursor line to valid range before drawing
	 int cursorLine = Math.Max(0, Math.Min(CursorPlace.iLine, linecount - 1));
	 float cursorY = cursorLine / (float)linecount * height;
	 args.DrawingSession.DrawLine(0, cursorY, width, cursorY, ActualTheme == ElementTheme.Light ? Color_Beam.InvertColorBrightness() : Color_Beam, 2f);
	}
  }
  catch (Exception ex)
  {
	ErrorOccured?.Invoke(this, new ErrorEventArgs(ex));
  }
 }

 private void CanvasSelection_Draw(CanvasControl sender, CanvasDrawEventArgs args)
 {
  try
  {
	if (VisibleLines.Count == 0)
	 return;

	// Defensive snapshot of search matches
	var searchMatches = SnapshotSafe(SearchMatches);

	if (!IsSelection)
	{
	 // draw search matches only
	 foreach (var match in searchMatches)
	 {
	  if (match == null) continue;
	  if (match.iLine >= VisibleLines[0].iLine && match.iLine <= VisibleLines.Last().iLine)
		DrawSelection(args.DrawingSession, match.iLine, match.iChar, match.iChar + match.Match.Length, SelectionType.SearchMatch);
	 }
	 return;
	}

	Place start = Selection.VisualStart;
	Place end = Selection.VisualEnd;

	// Ensure start <= end (by line, then by char)
	if (start.iLine > end.iLine || (start.iLine == end.iLine && start.iChar > end.iChar))
	{
	 var tmp = start;
	 start = end;
	 end = tmp;
	}

	// Clip to visible range
	int visibleFirst = VisibleLines[0].iLine;
	int visibleLast = VisibleLines.Last().iLine;

	if (end.iLine < visibleFirst || start.iLine > visibleLast)
	{
	 // selection outside visible area -> still draw search matches
	 foreach (var match in searchMatches)
	 {
	  if (match == null) continue;
	  if (match.iLine >= visibleFirst && match.iLine <= visibleLast)
		DrawSelection(args.DrawingSession, match.iLine, match.iChar, match.iChar + match.Match.Length, SelectionType.SearchMatch);
	 }
	 return;
	}

	// clamp start/end to visible region
	start.iLine = Math.Max(start.iLine, visibleFirst);
	end.iLine = Math.Min(end.iLine, visibleLast);
	if (start.iLine == VisibleLines[0].iLine)
	 start.iChar = Math.Max(start.iChar, 0);

	// Draw selection line by line
	for (int lp = start.iLine; lp <= end.iLine; lp++)
	{
	 if (lp < visibleFirst || lp > visibleLast) continue;
	 if (lp < 0 || lp >= Lines.Count) continue;

	 if (start.iLine == end.iLine)
	 {
	  // single-line selection
	  DrawSelection(args.DrawingSession, start.iLine, start.iChar, end.iChar);
	 }
	 else if (lp == start.iLine)
	 {
	  // first line: from start char to line end
	  DrawSelection(args.DrawingSession, lp, start.iChar, Lines[lp].Count + 1);
	 }
	 else if (lp > start.iLine && lp < end.iLine)
	 {
	  // full middle lines
	  DrawSelection(args.DrawingSession, lp, 0, Lines[lp].Count + 1);
	 }
	 else if (lp == end.iLine)
	 {
	  // last line: from 0 to end char
	  DrawSelection(args.DrawingSession, lp, 0, end.iChar);
	 }
	}

	// draw search matches on top
	foreach (var match in searchMatches)
	{
	 if (match == null) continue;
	 if (match.iLine >= visibleFirst && match.iLine <= visibleLast)
	  DrawSelection(args.DrawingSession, match.iLine, match.iChar, match.iChar + match.Match.Length, SelectionType.SearchMatch);
	}
  }
  catch (Exception ex)
  {
	ErrorOccured?.Invoke(this, new ErrorEventArgs(ex));
  }
 }

 private void CanvasText_Draw(CanvasControl sender, CanvasDrawEventArgs args)
 {
  try
  {
	// Stabilize DPI and antialiasing
	sender.DpiScale = XamlRoot.RasterizationScale > 1.0d ? 1.15f : 1.0f;
	args.DrawingSession.Antialiasing = CanvasAntialiasing.Aliased;

	if (VisibleLines == null || VisibleLines.Count == 0 || Lines == null || Lines.Count == 0)
	 return;

	var visible = VisibleLines;
	var linesRef = Lines;
	var textFmt = TextFormat ?? new CanvasTextFormat() { FontFamily = FontUri, FontSize = ScaledFontSize };
	float thickness = Math.Max(1, CharWidth / 6f);

	CanvasStrokeStyle indentStroke = _cachedIndentStroke;
	CanvasGeometry tabArrowGeom = _cachedTabArrowGeom;
	CanvasGeometry enterGeom = _cachedEnterGeom;

	// Draw only visible lines and their wrapped sublines
	int totalWrapsSoFar = 0;
	int visibleStartLineNumber = visible[0].iLine;
	int globalYOffset = -VisibleStartSubline * CharHeight;

	for (int v = 0; v < visible.Count; v++)
	{
	 var line = visible[v];
	 int iLine = line.iLine;

	 // starting y position for this logical line, includes already counted wrapped rows
	 int baseY = globalYOffset + CharHeight * (iLine - visibleStartLineNumber + totalWrapsSoFar);
	 int y = baseY;

	 // fill left background (line info area)
	 args.DrawingSession.FillRectangle(0, y, Width_Left - Width_TextIndent, CharHeight, Color_LeftBackground);

	 // If wrapping is enabled and WrappedLines available, use them; otherwise draw as single subline
	 var wrapped = (IsWrappingEnabled && line.WrappedLines != null && line.WrappedLines.Count > 0)
						? line.WrappedLines
						: new List<List<Char>> { LineToCharList(line) };

	 // iterate wrapped sublines
	 for (int wi = 0; wi < wrapped.Count; wi++)
	 {
	  var sub = wrapped[wi];
	  int visualPos = 0;

	  for (int ci = 0; ci < sub.Count; ci++)
	  {
		var c = sub[ci];

		if (c.C == '\t')
		{
		 int xTab = Width_Left + CharWidth * (visualPos - iCharOffset);
		 if (ShowControlCharacters && visualPos >= iCharOffset)
		 {
		  args.DrawingSession.DrawGeometry(tabArrowGeom, xTab, y, ActualTheme == ElementTheme.Light ? Color_WeakMarker.InvertColorBrightness() : Color_WeakMarker, thickness);
		 }
		 if (ShowIndentGuides != IndentGuide.None && visualPos >= iCharOffset)
		 {
		  args.DrawingSession.DrawLine(xTab + CharWidth / 3f, y, xTab + CharWidth / 3f, y + CharHeight, ActualTheme == ElementTheme.Light ? Color_FoldingMarkerUnselected.InvertColorBrightness() : Color_FoldingMarkerUnselected, 1.5f, indentStroke);
		 }
		 visualPos += TabLength;
		 continue;
		}

		if (visualPos >= iCharOffset && (visualPos - iCharOffset) < iVisibleChars)
		{
		 int x = Width_Left + CharWidth * (visualPos - iCharOffset);
		 Color drawColor;
		 if (c.T == Token.Key && IsInsideBrackets(new(ci, iLine)))
		  drawColor = ActualTheme == ElementTheme.Light ? EditorOptions.TokenColors[Token.Key].InvertColorBrightness() : EditorOptions.TokenColors[Token.Key];
		 else
		  drawColor = ActualTheme == ElementTheme.Light ? EditorOptions.TokenColors[c.T].InvertColorBrightness() : EditorOptions.TokenColors[c.T];

		 args.DrawingSession.DrawText(c.C.ToString(), x, y, drawColor, textFmt);
		}

		visualPos += 1;
	  } // chars

	  // draw control-char for Enter at logical line end
	  if (ShowControlCharacters && iLine < linesRef.Count - 1)
	  {
		int visualLengthOfSub = 0;
		foreach (var ch2 in sub)
		 visualLengthOfSub += ch2.C == '\t' ? TabLength : 1;
		int xEnter = Width_Left + CharWidth * (visualLengthOfSub - iCharOffset);
		args.DrawingSession.DrawGeometry(enterGeom, xEnter, y, ActualTheme == ElementTheme.Light ? Color_WeakMarker.InvertColorBrightness() : Color_WeakMarker, thickness);
	  }

	  // if not last wrapped subline, move down and count a wrap
	  if (wi < wrapped.Count - 1)
	  {
		y += CharHeight;
		totalWrapsSoFar++;
		args.DrawingSession.FillRectangle(0, y, Width_Left - Width_TextIndent, CharHeight, Color_LeftBackground);
	  }
	 } // wrapped sublines
	} // visible lines
  }
  catch (Exception ex)
  {
	ErrorOccured?.Invoke(this, new ErrorEventArgs(ex));
  }
 }

 private async void CodeWriter_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
 {
  try
  {
	if (isSelecting | IsFindPopupOpen) return;
	if (char.IsLetterOrDigit(args.Character) | char.IsSymbol(args.Character) | char.IsPunctuation(args.Character) | char.IsSeparator(args.Character) | char.IsSurrogate(args.Character))
	{
	 if (IsSelection)
	 {
	  TextAction_Delete(Selection);
	  Selection = new(Selection.VisualStart);
	 }

	 if (Language.CommandTriggerCharacters.Contains(args.Character))
	 {
	  SuggestionStart = CursorPlace;
	  //Suggestions = Commands;
	  SuggestionIndex = -1;

	  IsSuggesting = true;
	  IsSuggestingOptions = false;
	  Lbx_Suggestions.ScrollIntoView(Suggestions?.FirstOrDefault());
	 }
	 else if (!char.IsLetter(args.Character))
	 {
	  IsSuggesting = false;
	  IsSuggestingOptions = false;
	 }

   if (args.Character == ' ')
	 {
	  if (EditActionHistory.Count > 0)
	  {
		var last = EditActionHistory.Last();
		if (last.EditActionType == EditActionType.Add &&
			last.Selection?.VisualStart.iLine == CursorPlace.iLine &&
			(last.Selection?.VisualStart.iChar ?? 0) + (last.TextInvolved?.Length ?? 0) == CursorPlace.iChar)
		{
		 last.TextInvolved += " ";
		}
		else
		{
		 EditActionHistory.Add(new() { EditActionType = EditActionType.Add, TextInvolved = " ", Selection = Selection });
		}
	  }
	  else
	  {
		EditActionHistory.Add(new() { EditActionType = EditActionType.Add, TextInvolved = " ", Selection = Selection });
	  }
	  _startNewAddActionOnNextChar = true;
	  if (!IsSuggestingOptions)
	  {
		IsSuggesting = false;
		IsSuggestingOptions = false;
	  }
	 }
	 else
	 {
		if (_startNewAddActionOnNextChar)
		{
		 EditActionHistory.Add(new() { TextInvolved = args.Character.ToString(), EditActionType = EditActionType.Add, Selection = Selection });
		 _startNewAddActionOnNextChar = false;
		}
		else if (EditActionHistory.Count > 0)
		{
		  var last = EditActionHistory.Last();
		  if (last.EditActionType == EditActionType.Add &&
				last.Selection?.VisualStart.iLine == CursorPlace.iLine &&
				(last.Selection?.VisualStart.iChar ?? 0) + (last.TextInvolved?.Length ?? 0) == CursorPlace.iChar)
		  {
			 last.TextInvolved += args.Character.ToString();
		  }
		  else
		  {
			 EditActionHistory.Add(new() { TextInvolved = args.Character.ToString(), EditActionType = EditActionType.Add, Selection = Selection });
		  }
		}
		else
		{
		  EditActionHistory.Add(new() { TextInvolved = args.Character.ToString(), EditActionType = EditActionType.Add, Selection = Selection });
		}
	 }

	 // --- apply text change to model ---
	 Lines[CursorPlace.iLine].SetLineText(Lines[CursorPlace.iLine].LineText.Insert(CursorPlace.iChar, args.Character.ToString()));

	 // update wrapping immediately so selection drawing uses up-to-date WrappedLines
	 if (IsWrappingEnabled)
	 {
	  // recalc wraps for all lines (CalculateLineWraps is already defensive)
	  CalculateLineWraps(CursorPlace.iLine, 1);
	 }

	 // If IntelliSense triggers...
	 if (Language.EnableIntelliSense)
	  if (((args.Character == ',' | args.Character == ' ') && IsInsideBrackets(CursorPlace)) | Language.OptionsTriggerCharacters.Contains(args.Character))
	  {
		IsSuggestingOptions = true;
		SuggestionStart = CursorPlace;
		var command = GetCommandAtPosition(CursorPlace);
		IntelliSense intelliSense = command.Command;
		var argument = command.ArgumentsRanges?.FirstOrDefault(x => CursorPlace.iChar >= x.Start.iChar && CursorPlace.iChar <= x.End.iChar);
		if (argument != null)
		{
		 int argumentindex = command.ArgumentsRanges.IndexOf(argument);
		 if (intelliSense != null && argumentindex != -1)
		 {
		  if (intelliSense.ArgumentsList?.Count > argumentindex)
		  {
			SuggestionStart = CursorPlace + 1;
			Options = intelliSense.ArgumentsList[argumentindex]?.Parameters;
			AllOptions = Suggestions = Options.Select(x =>
			{
			 if (x is KeyValue keyValue)
			 {
			  keyValue.Snippet = "=";
			  string options = "";
			  if (keyValue.Values != null)
			  {
				if (keyValue.Values.Count > 5)
				 options = string.Join("|", keyValue.Values.Take(5)) + "|...";
				else
				 options = string.Join("|", keyValue.Values);
				keyValue.Options = options;
			  }
			  keyValue.IntelliSenseType = IntelliSenseType.Argument;
			 }
			 return (Suggestion)x;
			}
			).ToList();
			IsSuggesting = true;
		  }
		 }
		}
	  }

	 if (Language.AutoClosingPairs.Keys.Contains(args.Character))
	 {
	  if (CursorPlace.iChar == Lines[CursorPlace.iLine].Count)
		Lines[CursorPlace.iLine].SetLineText(Lines[CursorPlace.iLine].LineText + Language.AutoClosingPairs[args.Character]);
	  else
		Lines[CursorPlace.iLine].SetLineText(Lines[CursorPlace.iLine].LineText.Insert(CursorPlace.iChar + 1, Language.AutoClosingPairs[args.Character].ToString()));
	 }

	 // advance selection / caret
	 Selection = new(Selection.VisualStart + 1);
	 iCharPosition = CursorPlace.iChar;
	 preferredVisualColumn = GetVisualColumnsForLine(CursorPlace.iLine, CursorPlace.iChar);

	 // ensure visual update immediately: invalidate selection canvas + text canvas
	 //CanvasSelection.Invalidate();
	 CanvasText.Invalidate();
	 //CanvasBeam.Invalidate();
	 //CanvasScrollbarMarkers.Invalidate();
	 //CanvasLineInfo.Invalidate();

	 // keep existing async update/maintenance calls
	 UpdateText();
	 textChanged();
	 FilterSuggestions();

	 IsFindPopupOpen = false;
	 args.Handled = true;
	}
  }
  catch (Exception ex)
  {
	ErrorOccured?.Invoke(this, new ErrorEventArgs(ex));
  }
 }

 private void CurrentLineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
 {
  if (!(d as CodeWriter).IsSettingValue)
  {
	Selection = new(CurrentLine);
	CenterView();
  }
 }

 private void DrawSelection(CanvasDrawingSession session, int Line, int StartChar, int EndChar, SelectionType selectionType = SelectionType.Selection)
 {
  // Defensive: clamp line index & visible
  if (VisibleLines == null || VisibleLines.Count == 0) return;
  if (Line < VisibleLines[0].iLine || Line > VisibleLines.Last().iLine) return;

  // Take defensive snapshot of Lines to avoid race when accessing per-line data
  var linesSnapshot = SnapshotSafe(Lines);
  if (Line < 0 || Line >= linesSnapshot.Length) return;

  // normalize bounds
  int start = Math.Max(0, StartChar);
  int end = Math.Min(EndChar, (linesSnapshot[Line]?.Count ?? 0) + 1); // allow caret at end

  // Draw per-character using PlaceToPoint for correct visual positions (handles wrapping/tabs robustly)
  for (int i = start; i < end; i++)
  {
	try
	{
	 // compute point for this character position (left edge)
	 Point p = PlaceToPoint(new Place(i, Line));

	 int x = (int)p.X;
	 int y = (int)p.Y;

	 // Draw selection cell width: tabs span TabLength columns
	 int w = 1;
	 var lineRef = linesSnapshot[Line];
	 if (lineRef != null && i < lineRef.Count && lineRef[i].C == '\t') w = TabLength;

	 DrawSelectionCell(session, x, y, selectionType, w);
	}
	catch
	{
	 // Swallow per-cell exceptions to keep drawing stable
	 continue;
	}
  }
 }

 private void DrawSelectionCell(CanvasDrawingSession session, int x, int y, SelectionType selectionType, int w = 1)
 {
  // Draws a single selection cell. Colors depend on selection type.
  Color color = Color_Selection;
  if (selectionType == SelectionType.SearchMatch)
  {
	color = Color.FromArgb(255, 60, 60, 60);
  }
  else if (selectionType == SelectionType.WordLight)
  {
	color = Color.FromArgb(255, 60, 60, 200);
  }

  session.FillRectangle(x, y, CharWidth * w + 1, CharHeight + 1, ActualTheme == ElementTheme.Light ? color.InvertColorBrightness() : color);
 }

 private CanvasGeometry _cachedTabArrowGeom = null;
 private CanvasGeometry _cachedEnterGeom = null;
 private CanvasStrokeStyle _cachedIndentStroke = null;
 private double _cachedDpiScaleForGeom = -1.0;
 private int _cachedCharWidthForGeom = -1;
 private int _cachedCharHeightForGeom = -1;

 private async Task DrawText(bool sizechanged = false, bool textchanged = false)
 {
  try
  {

	if (sizechanged && XamlRoot != null)
	{
	 TextFormat = new CanvasTextFormat()
	 {
	  FontFamily = FontUri,
	  FontSize = ScaledFontSize
	 };
	 Size size = MeasureTextSize(CanvasDevice.GetSharedDevice(), "M", TextFormat);
	 Size sizew = MeasureTextSize(CanvasDevice.GetSharedDevice(), "M", TextFormat);
	 CharHeight = (int)(size.Height * 2f);
	 float widthfactor = 1.1f;
	 if (Font.StartsWith("Cascadia"))
	 {
	  widthfactor = 1.35f;
	 }
	 CharWidth = (int)(sizew.Width * widthfactor);
	 var currentDpi = XamlRoot.RasterizationScale;
	 if (_cachedTabArrowGeom == null
		  || _cachedCharWidthForGeom != CharWidth
		  || _cachedCharHeightForGeom != CharHeight
		  || Math.Abs(_cachedDpiScaleForGeom - currentDpi) > 0.001)
	 {
	  // Dispose vorheriger Ressourcen (sofern nötig)
	  try { _cachedTabArrowGeom?.Dispose(); } catch { }
	  try { _cachedEnterGeom?.Dispose(); } catch { }
	  try { _cachedIndentStroke?.Dispose(); } catch { }

	  var device = CanvasDevice.GetSharedDevice();
	  // Tab arrow
	  var pbTab = new CanvasPathBuilder(device);
	  pbTab.BeginFigure(CharWidth * 0.2f, CharHeight / 2f);
	  pbTab.AddLine(CharWidth * (TabLength - 0.2f), CharHeight / 2f);
	  pbTab.EndFigure(CanvasFigureLoop.Open);
	  pbTab.BeginFigure(CharWidth * (TabLength - 0.5f), CharHeight * 1f / 4f);
	  pbTab.AddLine(CharWidth * (TabLength - 0.2f), CharHeight / 2f);
	  pbTab.AddLine(CharWidth * (TabLength - 0.5f), CharHeight * 3f / 4f);
	  pbTab.EndFigure(CanvasFigureLoop.Open);
	  _cachedTabArrowGeom = CanvasGeometry.CreatePath(pbTab);

	  // Enter geometry
	  var pbEnter = new CanvasPathBuilder(device);
	  pbEnter.BeginFigure(CharWidth * 0.9f, CharHeight * 1f / 3f);
	  pbEnter.AddLine(CharWidth * 0.9f, CharHeight * 3f / 4f);
	  pbEnter.AddLine(0, CharHeight * 3f / 4f);
	  pbEnter.EndFigure(CanvasFigureLoop.Open);
	  pbEnter.BeginFigure(CharWidth * 0.4f, CharHeight * 2f / 4f);
	  pbEnter.AddLine(CharWidth * 0.1f, CharHeight * 3f / 4f);
	  pbEnter.AddLine(CharWidth * 0.4f, CharHeight);
	  pbEnter.EndFigure(CanvasFigureLoop.Open);
	  _cachedEnterGeom = CanvasGeometry.CreatePath(pbEnter);

	  // Indent stroke (DashStyle)
	  _cachedIndentStroke = new CanvasStrokeStyle()
	  {
		DashStyle = ShowIndentGuides != IndentGuide.None && ShowIndentGuides == IndentGuide.Line ? CanvasDashStyle.Solid : CanvasDashStyle.Dash
	  };

	  _cachedCharWidthForGeom = CharWidth;
	  _cachedCharHeightForGeom = CharHeight;
	  _cachedDpiScaleForGeom = currentDpi;
	 }
	}

	if (VerticalScroll != null && HorizontalScroll != null && Lines != null)
	{
	 // Calculate visual total rows when wrapping is enabled
	 int totalVisualRows = 0;
	 if (IsWrappingEnabled)
	 {
	  // defensive snapshot to avoid concurrent modification
	  var linesSnapshot = SnapshotSafe(Lines);
	  foreach (var l in linesSnapshot)
	  {
		if (l == null) continue;
		totalVisualRows += (l.WrappedLines != null && l.WrappedLines.Count > 0) ? l.WrappedLines.Count : 1;
	  }
	 }
	 else
	 {
	  totalVisualRows = Lines.Count;
	 }

	 // Set vertical scroll maximum based on visual rows (sublines) so wheel scroll can move by sublines
	 VerticalScroll.Maximum = Math.Max(0, (totalVisualRows + 3) * CharHeight - Scroll.ActualHeight);
	 VerticalScroll.SmallChange = CharHeight;
	 VerticalScroll.LargeChange = CharHeight * iVisibleLines;
	 VerticalScroll.Visibility = totalVisualRows * CharHeight > TextControl.ActualHeight ? Visibility.Visible : Visibility.Collapsed;

	 HorizontalScroll.SmallChange = CharWidth;
	 HorizontalScroll.LargeChange = CharWidth;

	 // Determine visual start row and map to logical start line + subline offset
	 int visualStartRow = Math.Max(0, (int)(VerticalScroll.Value / CharHeight));
	 int startLineIndex = 0;
	 int startSubIndex = 0;
	 if (Lines.Count > 0)
	  if (IsWrappingEnabled)
	  {
		int acc = 0;
		for (int i = 0; i < Lines.Count; i++)
		{
		 var wraps = (Lines[i].WrappedLines != null && Lines[i].WrappedLines.Count > 0) ? Lines[i].WrappedLines.Count : 1;
		 if (visualStartRow < acc + wraps)
		 {
		  startLineIndex = i;
		  startSubIndex = Math.Max(0, visualStartRow - acc);
		  break;
		 }
		 acc += wraps;
		}
	  }
	  else
	  {
		startLineIndex = Math.Min(Math.Max(0, visualStartRow), Lines.Count - 1);
		startSubIndex = 0;
	  }

	 VisibleStartSubline = startSubIndex;

	 // Number of visual rows to fill (viewport) + safety margin
	 int rowsNeeded = (int)(Scroll.ActualHeight / CharHeight) + 2;

	 VisibleLines.Clear();
	 int rowsAdded = 0;
	 if (Lines.Count > 0)
	  for (int i = startLineIndex; i < Lines.Count && rowsAdded < rowsNeeded; i++)
	  {
		var l = Lines[i];
		if (l != null)
		{
		 VisibleLines.Add(l);
		 rowsAdded += IsWrappingEnabled ? (l.WrappedLines != null && l.WrappedLines.Count > 0 ? l.WrappedLines.Count : 1) : 1;
		}
	  }

	 if (textchanged)
	 {
	  if (!_isLoadingLines)
	  {
		Width_LeftMargin = ShowLineNumbers ? CharWidth : 0;
		Width_LineNumber = ShowLineNumbers ? Math.Max(Width_LineNumber, CharWidth * IntLength(Lines.Count)) : 0;
		Width_FoldingMarker = IsFoldingEnabled && Language.FoldingPairs != null ? CharWidth : 0;
		Width_ErrorMarker = ShowLineNumbers ? CharWidth / 2 : 0;
		Width_WarningMarker = ShowLineMarkers ? CharWidth / 2 : 0;
	  }

	  // calculate horizontal maximum based on characters
	  if (!_isLoadingLines)
	  {
		maxchars = 0;
		for (int i = 0; i < Lines.Count; i++)
		{
		 Line l = Lines[i];
		 maxchars = Math.Max(l.Count + l.Indents * (TabLength - 1) + 1, maxchars);
		}
	  }
	  if (!IsWrappingEnabled)
	  {
		HorizontalScroll.Maximum = (maxchars + 1) * CharWidth - Scroll.ActualWidth + Width_Left;
		HorizontalScroll.Visibility = maxchars * CharWidth > TextControl.ActualWidth ? Visibility.Visible : Visibility.Collapsed;
	  }
	  else
	  {
		HorizontalScroll.Maximum = 0;
		HorizontalScroll.Visibility = Visibility.Collapsed;
	  }

	  VerticalScroll.ViewportSize = Scroll.ActualHeight;
	  HorizontalScroll.ViewportSize = Scroll.ActualWidth;

	  // Update folding pairs asynchronously when lines changed (skip during loading — Phase 2 handles it)
	  if (!_isLoadingLines && IsFoldingEnabled && Language?.FoldingPairs != null)
	  {
		Language lang = Language;
		await Task.Run(() => updateFoldingPairs(lang));
	  }
	 }

	 // Calculate wraps only when wrapping is enabled; restrict to visible logical lines to keep cost independent of document size
	 if (IsWrappingEnabled)
	 {
	  int visibleFirst = Math.Max(0, startLineIndex);
	  int visibleCount = Math.Max(1, VisibleLines.Count);
	  CalculateLineWraps(visibleFirst, visibleCount);
	 }

	 DispatcherQueue.TryEnqueue(() =>
	 {
	  CanvasBeam.Invalidate();
	  CanvasSelection.Invalidate();
	  if (!_isLoadingLines)
		CanvasText.Invalidate();
	  CanvasScrollbarMarkers.Invalidate();
	  if (!_isLoadingLines)
		CanvasLineInfo.Invalidate();
	 });
	}
  }
  catch (Exception ex)
  {
	ErrorOccured?.Invoke(this, new ErrorEventArgs(ex));
  }
 }

 private void EditActionHistoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
 {
  var history = (e.NewValue as ObservableCollection<EditAction>);
  history.CollectionChanged += EditActionHistory_CollectionChanged;
  CanUndo = history.Count > 0;
  InvertedEditActionHistory = new(history.Reverse());
 }

 private int GetWrappingLinesOffset(int iline)
 {
  int visualline = 0;
  int ilineoffset = 0;
  int wraplines = 0;
  if (IsWrappingEnabled)
  {
	Action getLine = delegate //() =>
	{
	 int iwrappedLines = 0;
	 foreach (var line in VisibleLines)
	 {
	  for (int wrappingLine = 0; wrappingLine < line.WrappedLines.Count; wrappingLine++)
	  {
		if (wrappingLine != 0)
		 iwrappedLines++;
		if (visualline == iline + iwrappedLines)
		{
		 wraplines = iwrappedLines;
		 return;
		}
		visualline++;
	  }
	 }
	};
	getLine();
	return wraplines;
  }
  else return 0;
 }

 private async Task InitializeLines(string text)
 {
  VisibleLines.Clear();
  Lines.Clear();
  CursorPlaceHistory.Clear();
  IsFindPopupOpen = false;

  Language lang = Language;

  if (text == null) return;

  // Normalize line endings on background thread and split
  string[] rawLines = await Task.Run(() =>
  {
	text = text.Replace("\r\n", "\n").Replace("\r", "\n");
	return text.Split("\n", StringSplitOptions.None);
  });

  // Phase 1: Blockwise load — parse blocks of 500 lines in background, then add to Lines + update UI
  const int blockSize = 500;
  int totalLines = rawLines.Length;
  bool innerLang = false;

  // Ensure CharWidth/CharHeight are computed before setting widths
  if (XamlRoot != null)
  {
  TextFormat = new CanvasTextFormat() { FontFamily = FontUri, FontSize = ScaledFontSize };
  Size size = MeasureTextSize(CanvasDevice.GetSharedDevice(), "M", TextFormat);
  Size sizew = MeasureTextSize(CanvasDevice.GetSharedDevice(), "M", TextFormat);
  CharHeight = (int)(size.Height * 2f);
  float widthfactor = Font.StartsWith("Cascadia") ? 1.35f : 1.1f;
  CharWidth = (int)(sizew.Width * widthfactor);
  }

  // Pre-set all layout widths to final values so CanvasLineInfo doesn't resize during loading
  Width_LeftMargin = ShowLineNumbers ? CharWidth : 0;
  Width_LineNumber = ShowLineNumbers ? CharWidth * IntLength(totalLines) : 0;
  Width_FoldingMarker = IsFoldingEnabled && Language.FoldingPairs != null ? CharWidth : 0;
  Width_ErrorMarker = ShowLineNumbers ? CharWidth / 2 : 0;
  Width_WarningMarker = ShowLineMarkers ? CharWidth / 2 : 0;

  _isLoadingLines = true;
  maxchars = 0;
  for (int blockStart = 0; blockStart < totalLines; blockStart += blockSize)
  {
  int blockEnd = Math.Min(blockStart + blockSize, totalLines);
  bool innerLangCapture = innerLang;
  int blockStartCapture = blockStart;
  int tabLen = TabLength;

  var (block, innerLangResult, blockMaxChars) = await Task.Run(() =>
  {
	var result = new List<Line>(blockEnd - blockStartCapture);
	bool il = innerLangCapture;
	int bmc = 0;

	for (int i = blockStartCapture; i < blockEnd; i++)
	{
	 string line = rawLines[i];
	 Language lg = lang;

	 if (lang.NestedLanguages != null)
	  foreach (var nestedlang in lang.NestedLanguages)
	  {
		Match endmatch = Regex.Match(line, nestedlang.RegexEnd);
		if (endmatch.Success)
		 il = false;
		if (il)
		 lg = Languages.LanguageList.FirstOrDefault(x => x.Name == nestedlang.InnerLanguage) ?? lang;
		else
		 lg = lang;
		Match startmatch = Regex.Match(line, nestedlang.RegexStart);
		if (startmatch.Success)
		 il = true;
	  }

	 Line l = new Line(lg) { LineNumber = blockStartCapture + (i - blockStartCapture) + 1 };
	 l.SetLineTextRaw(line);
	 l.Save();
	 result.Add(l);
	 bmc = Math.Max(bmc, l.Count + l.Indents * (tabLen - 1) + 1);
	}
	return (result, il, bmc);
  });

  innerLang = innerLangResult;
  maxchars = Math.Max(maxchars, blockMaxChars);
	Lines.AddRange(block);

	// Update UI after each block so text + scrollbar are visible immediately
	if (isCanvasLoaded && Lines.Count > 0)
	{
	 bool isFirstBlock = blockStart == 0;
	 await DrawText(isFirstBlock, isFirstBlock);
	 if (isFirstBlock && !IsInitialized)
	 {
	  IsInitialized = true;
	  Initialized?.Invoke(this, null);
	 }
	 CanvasText.Invalidate();
	 CanvasLineInfo.Invalidate();
	}
  }

  // Final DrawText with textchanged=true for maxchars/horizontal scroll
  if (isCanvasLoaded && Lines.Count > 0)
  await DrawText(true, true);

  // Phase 2: Run tokenizer asynchronously and refresh display
  Language langCapture = Language;
  await Task.Run(() =>
  {
  var linesSnapshot = SnapshotSafe(Lines);
  foreach (var l in linesSnapshot)
  {
	l?.Tokenize();
  }
  updateFoldingPairs(langCapture);
  });

  // Loading complete — allow DrawText to update widths again
  _isLoadingLines = false;

  // Single refresh after tokenization completes — clean transition from monochrome to colored
  DispatcherQueue.TryEnqueue(() =>
  {
  CanvasText.Invalidate();
  CanvasLineInfo.Invalidate();
  });
 }

 private void KeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
 {
  if (invoked)
  {
	args.Handled = true;
	invoked = false;
  }
  else
  {
	invoked = true;
  }
 }

 private void Lbx_Suggestions_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
 {
  InsertSuggestion();
  CanvasText.Invalidate();
 }

 private void Lbx_Suggestions_KeyDown(object sender, KeyRoutedEventArgs e)
 {
  try
  {
	if (!isSelecting && (e.Key == VirtualKey.Enter | e.Key == VirtualKey.Tab))
	{
	 if (IsSuggesting)
	 {
	  InsertSuggestion();
	  CanvasText.Invalidate();
	  Focus(FocusState.Keyboard);
	 }
	 e.Handled = true;
	}
  }
  catch { }
 }

 private Size MeasureTextSize(CanvasDevice device, string text, CanvasTextFormat textFormat, float limitedToWidth = 0.0f, float limitedToHeight = 0.0f)
 {
  CanvasTextLayout layout = new(device, text, textFormat, limitedToWidth, limitedToHeight);

  double width = layout.DrawBounds.Width;
  double height = layout.DrawBounds.Height;

  return new(width, height);
 }

 private void NormalArrowPointerEntered(object sender, PointerRoutedEventArgs e)
 {
  ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
  //Cursor = new InputCursor(CoreCursorType.Arrow, 1);
 }

 private void OnShowScrollbarsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
 {
  if ((bool)e.NewValue)
	HorizontalScroll.Style = VerticalScroll.Style = Resources["AlwaysExpandedScrollBar"] as Style;
  else
  {
	HorizontalScroll.Style = VerticalScroll.Style = Application.Current.Resources["DefaultScrollBarStyle"] as Style;
  }
 }
 private Point PlaceToPoint(Place currentplace)
 {
  // Defensive snapshots to avoid race conditions with Lines/VisibleLines modification.
  var linesSnapshot = SnapshotSafe(Lines);
  var visibleSnapshot = SnapshotSafe(VisibleLines).ToList();

  // Defensive checks
  if (linesSnapshot.Length == 0 || visibleSnapshot.Count == 0)
	return new Point(0, 0);

  // compute visual columns (expand tabs) up to currentplace.iChar
  int visualColsBefore = 0;
  if (currentplace.iLine >= 0 && currentplace.iLine < linesSnapshot.Length)
  {
	for (int i = 0; i < currentplace.iChar && i < (linesSnapshot[currentplace.iLine]?.Count ?? 0); i++)
	 visualColsBefore += (linesSnapshot[currentplace.iLine][i].C == '\t') ? TabLength : 1;
  }

  // If wrapping disabled use simple vertical calculation but keep expanded-column horizontal logic
  if (!IsWrappingEnabled)
  {
	int startline = visibleSnapshot.Count > 0 ? visibleSnapshot[0].iLine : 0;
	int y = (currentplace.iLine - startline) * CharHeight;
	int x = Width_Left + (visualColsBefore - iCharOffset) * CharWidth;
	return new Point(x, y);
  }

  // Wrapping enabled: compute visual row index explicitly (visualIndex)
  // Start visualIndex considering VisibleStartSubline offset
  int visualIndex = -VisibleStartSubline;
  foreach (var vl in visibleSnapshot)
  {
	if (vl == null) continue;
	var wraps = (vl.WrappedLines != null && vl.WrappedLines.Count > 0) ? vl.WrappedLines : new List<List<Char>> { LineToCharList(vl) };

	// If this is a prior logical line -> add all its wrapped rows
	if (vl.iLine < currentplace.iLine)
	{
	 visualIndex += wraps.Count;
	 continue;
	}

	// If this is the caret's logical line -> find which wrapped subline contains the caret
	if (vl.iLine == currentplace.iLine)
	{
	 // total logical characters in the line
	 int totalChars = vl.Count;

	 // If caret index is at or beyond line end, place at last subline end
	 if (currentplace.iChar >= totalChars)
	 {
	  int lastSub = Math.Max(0, wraps.Count - 1);
	  int y = visualIndex * CharHeight + lastSub * CharHeight;

	  // visual columns = sum of expanded widths of all chars in last subline
	  int visualColsLastSub = 0;
	  var lastSubList = wraps[lastSub] ?? new List<Char>();
	  for (int ci = 0; ci < lastSubList.Count; ci++)
		visualColsLastSub += lastSubList[ci].C == '\t' ? TabLength : 1;

	  int x = Width_Left + (visualColsLastSub - iCharOffset) * CharWidth;
	  return new Point(x, y);
	 }

	 // Normal case: find subline where caret index falls
	 int cumChars = 0;
	 int targetSub = 0;
	 int charIndexInSub = 0;
	 for (int si = 0; si < wraps.Count; si++)
	 {
	  var sub = wraps[si] ?? new List<Char>();
	  if (currentplace.iChar < cumChars + sub.Count)
	  {
		targetSub = si;
		charIndexInSub = currentplace.iChar - cumChars;
		break;
	  }
	  cumChars += sub.Count;
	 }

	 int yBase = visualIndex * CharHeight + targetSub * CharHeight;

	 // compute visual columns inside the target subline (expand tabs)
	 int visualColsInSub = 0;
	 var targetSubList = wraps[targetSub] ?? new List<Char>();
	 for (int ci = 0; ci < charIndexInSub && ci < targetSubList.Count; ci++)
	  visualColsInSub += targetSubList[ci].C == '\t' ? TabLength : 1;

	 int xPos = Width_Left + (visualColsInSub - iCharOffset) * CharWidth;
	 return new Point(xPos, yBase);
	}

	visualIndex += wraps.Count;
  }

  // Fallback: top-left of text area
  return new Point(Width_Left, 0);
 }

 private async Task<Place> PointToPlace(Point currentpoint)
 {
  if (SnapshotSafe(VisibleLines).Length == 0)
	return new Place(0, 0);

  try
  {
	// Defensive snapshots
	var visibleSnapshot = SnapshotSafe(VisibleLines).ToList();
	var linesSnapshot = SnapshotSafe(Lines);

	// Visual row inside the canvas (0-based)
	int visualRow = Math.Max(0, (int)(currentpoint.Y / CharHeight));

	// adjust the visual row by the starting subline offset
	int adjustedVisualRow = visualRow + VisibleStartSubline;

	// find logical line and subline index that correspond to adjustedVisualRow
	int visualIndex = 0;
	int targetLine = visibleSnapshot[0].iLine;
	int subIndex = 0;
	bool found = false;

	foreach (var vl in visibleSnapshot)
	{
	 var wraps = (IsWrappingEnabled && vl.WrappedLines != null && vl.WrappedLines.Count > 0)
		 ? vl.WrappedLines
		 : new List<List<Char>> { LineToCharList(vl) };

	 if (adjustedVisualRow < visualIndex + wraps.Count)
	 {
	  targetLine = vl.iLine;
	  subIndex = adjustedVisualRow - visualIndex;
	  found = true;
	  break;
	 }
	 visualIndex += wraps.Count;
	}

	if (!found)
	{
	 // clicked outside visible area -> clamp to last visible line/subline
	 var last = visibleSnapshot.LastOrDefault();
	 if (last == null) return new Place(0, 0);
	 targetLine = last.iLine;
	 subIndex = (IsWrappingEnabled && last.WrappedLines != null) ? last.WrappedLines.Count - 1 : 0;
	}

	// get wraps for target logical line, guard missing or out-of-range lines
	if (targetLine < 0 || targetLine >= linesSnapshot.Length)
	 return new Place(0, 0);

	var targetLineRef = linesSnapshot[targetLine];
	var targetWraps = (IsWrappingEnabled && targetLineRef?.WrappedLines != null && targetLineRef.WrappedLines.Count > 0)
		? targetLineRef.WrappedLines
		: new List<List<Char>> { LineToCharList(targetLineRef ?? new Line(Language)) };

	// clamp subIndex
	subIndex = Math.Max(0, Math.Min(subIndex, targetWraps.Count - 1));

	// Compute absolute character index at start of this subline
	int charsBeforeSub = 0;
	for (int si = 0; si < subIndex; si++)
	 charsBeforeSub += targetWraps[si]?.Count ?? 0;

	// If the subline is empty, return start of logical line
	var subline = targetWraps[subIndex] ?? new List<Char>();
	if (subline.Count == 0)
	 return new Place(charsBeforeSub, targetLine);

	// Use probing to match rendering but compute "nextX" from the current character cell width
	double clickX = currentpoint.X;
	int bestIndex = 0;
	bool foundIndex = false;

	for (int ci = 0; ci <= subline.Count; ci++)
	{
	 // left edge of character at absolute char index (charsBeforeSub + ci)
	 Point left = PlaceToPoint(new Place(charsBeforeSub + ci, targetLine));
	 double leftX = left.X;

	 if (ci < subline.Count)
	 {
	  // Determine this character's visual width (tabs expanded)
	  int wcols = subline[ci].C == '\t' ? TabLength : 1;
	  double nextX = leftX + wcols * CharWidth;

	  double mid = (leftX + nextX) / 2.0;
	  if (clickX < mid)
	  {
		bestIndex = ci;
		foundIndex = true;
		break;
	  }
	 }
	 else
	 {
	  // end of subline -> place at end
	  bestIndex = ci;
	  foundIndex = true;
	  break;
	 }
	}

	if (!foundIndex)
	{
	 // defensive fallback
	 bestIndex = 0;
	}

	int finalChar = Math.Min(charsBeforeSub + bestIndex, targetLineRef?.Count ?? 0);
	return new Place(finalChar, targetLine);
  }
  catch
  {
	return new Place(0, 0);
  }
 }

 private void RecalcLineNumbers(int startLine = -1)
 {
  int start = startLine >= 0 ? startLine : CursorPlace.iLine;
  start = Math.Max(0, start);
  for (int i = start; i < Lines.Count; i++)
	Lines[i].LineNumber = i + 1;
 }

 private int preferredVisualColumn = -1;
 private int GetVisualColumnsForLine(int lineIndex, int charCount)
 {
  // Defensive snapshot
  var linesSnap = SnapshotSafe(Lines);
  if (lineIndex < 0 || lineIndex >= linesSnap.Length) return 0;
  var line = linesSnap[lineIndex];
  if (line == null) return 0;

  int upto = Math.Min(charCount, line.Count);
  int visual = 0;
  for (int i = 0; i < upto; i++)
	visual += (line[i].C == '\t') ? TabLength : 1;
  return visual;
 }
 private async void Scroll_KeyDown(object sender, KeyRoutedEventArgs e)
 {
  try
  {
	if (e.Key == VirtualKey.Shift)
	{
	 shiftKeyState = CoreVirtualKeyStates.Down;
	 e.Handled = true;
	 return;
	}
	else if (e.Key == VirtualKey.Control)
	{
	 controlKeyState = CoreVirtualKeyStates.Down;
	 e.Handled = true;
	 return;
	}

	bool shiftdown = shiftKeyState == CoreVirtualKeyStates.Down;

	if (!isSelecting & !IsFindPopupOpen)
	{
	 string storetext = "";
	 Place newplace = new(CursorPlace);
	 switch (e.Key)
	 {
	  case VirtualKey.Escape:
		IsSuggesting = false;
		break;

	  case VirtualKey.Tab:
		if (IsSuggesting)
		{
		 InsertSuggestion();
		 CanvasText.Invalidate();
		 e.Handled = true;
		 break;
		}
        // Record tab insertion as operation-only
        EditActionHistory.Add(new() { Selection = Selection, TextInvolved = "\t", EditActionType = EditActionType.Add });
		if (IsSelection)
		{
		 Place start = new(Selection.VisualStart);
		 Place end = new(Selection.VisualEnd);
		 if (shiftKeyState != CoreVirtualKeyStates.None)
		 {
		  for (int iLine = Selection.VisualStart.iLine; iLine <= Selection.VisualEnd.iLine; iLine++)
		  {
			if (Lines[iLine].LineText.StartsWith("\t"))
			{
			 Lines[iLine].SetLineText(Lines[iLine].LineText.Remove(0, 1));
			 if (iLine == Selection.VisualStart.iLine)
			  start -= 1;
			 else if (iLine == Selection.VisualEnd.iLine)
			  end -= 1;
			}
		  }
		  Selection = new(start, end);
		 }
		 else
		 {
		  for (int iLine = Selection.VisualStart.iLine; iLine <= Selection.VisualEnd.iLine; iLine++)
		  {
			Lines[iLine].SetLineText(Lines[iLine].LineText.Insert(0, "\t"));
		  }
		  Selection = new(Selection.Start + 1, Selection.End + 1);
		 }
		}
		else
		{
		 if (shiftKeyState == CoreVirtualKeyStates.Down)
		 {
		  if (Lines[CursorPlace.iLine].LineText.StartsWith("\t"))
		  {
			Lines[CursorPlace.iLine].SetLineText(Lines[CursorPlace.iLine].LineText.Remove(0, 1));
			Selection = new(CursorPlace - 1);
		  }
		 }
		 else
		 {
		  Lines[CursorPlace.iLine].SetLineText(Lines[CursorPlace.iLine].LineText.Insert(CursorPlace.iChar, "\t"));
		  Selection = new(CursorPlace + 1);
		 }
		}

		if (IsWrappingEnabled)
		{
		 // If selection, recalc for the whole selected region; otherwise only caret line
		 if (IsSelection)
		 {
		  int start = Selection.VisualStart.iLine;
		  int len = Selection.VisualEnd.iLine - Selection.VisualStart.iLine + 1;
		  CalculateLineWraps(start, len);
		 }
		 else
		 {
		  CalculateLineWraps(CursorPlace.iLine, 1);
		 }
		}

		UpdateText();
		DispatcherQueue.TryEnqueue(() =>
		{
		 CanvasText.Invalidate();
		});
		e.Handled = true;
		break;

	  case VirtualKey.Enter:
		if (IsSuggesting)
		{
		 if (SuggestionIndex == -1)
		 {
		  IsSuggesting = false;
		 }
		 else
		 {
		  InsertSuggestion();
		  CanvasText.Invalidate();
		 }
		 e.Handled = true;
		 break;
		}
		if (controlKeyState == CoreVirtualKeyStates.Down)
		{
		 break;
		}
		if (IsSelection)
		{
		 TextAction_Delete(Selection);
		}
		  // Record newline insertion as operation-only; capture saved baseline for undo
		  EditActionHistory.Add(new() { Selection = new(Selection.VisualStart), TextInvolved = "\n", EditActionType = EditActionType.Add, SavedTexts = new() { Lines[CursorPlace.iLine].SavedText } });
		if (CursorPlace.iChar < Lines[CursorPlace.iLine].Count)
		{
		 storetext = Lines[CursorPlace.iLine].LineText.Substring(CursorPlace.iChar);
		 Lines[CursorPlace.iLine].SetLineText(Lines[CursorPlace.iLine].LineText.Remove(CursorPlace.iChar));
		}
		string indents = string.Concat(Enumerable.Repeat("\t", Lines[CursorPlace.iLine].Indents));
		var newline = new Line(Language) { LineNumber = CursorPlace.iLine, IsUnsaved = true };

		newline.SetLineText(indents + storetext);
		Lines.Insert(CursorPlace.iLine + 1, newline);

		for (int i = CursorPlace.iLine + 1; i < Lines?.Count; i++)
		 Lines[i]?.LineNumber = i + 1;
		Place newselect = CursorPlace;
		newselect.iLine++;
		newselect.iChar = Lines[CursorPlace.iLine].Indents;
		Selection = new Range(newselect, newselect);

		if (IsWrappingEnabled) CalculateLineWraps(Math.Max(0, CursorPlace.iLine - 1), 3);

		await DrawText(false, true);
		checkUnsavedChanges();
		e.Handled = true;
		break;

	  case VirtualKey.Delete:
		if (!IsSelection)
		{
		 if (CursorPlace.iChar == Lines[CursorPlace.iLine].Count && CursorPlace.iLine < Lines.Count - 1)
		 {
			 // Record join-lines (newline removal) as operation-only; capture saved baselines for undo
			 EditActionHistory.Add(new() { Selection = Selection, EditActionType = EditActionType.Remove, TextInvolved = "\n", SavedTexts = new() { Lines[CursorPlace.iLine].SavedText, Lines[CursorPlace.iLine + 1].SavedText } });
		  storetext = Lines[CursorPlace.iLine + 1].LineText;
		  Lines.RemoveAt(CursorPlace.iLine + 1);
		  Lines[CursorPlace.iLine].AddToLineText(storetext);

		  RecalcLineNumbers();

		  if (IsWrappingEnabled) CalculateLineWraps(Math.Max(0, CursorPlace.iLine - 1), 2);

		  await DrawText(false, true);
		  checkUnsavedChanges();
		 }
		 else if (CursorPlace.iChar < Lines[CursorPlace.iLine].Count)
		 {
          // Record single-char removal as operation-only
          EditActionHistory.Add(new() { Selection = Selection, EditActionType = EditActionType.Remove, TextInvolved = Lines[CursorPlace.iLine].LineText[CursorPlace.iChar].ToString() });
		  Lines[CursorPlace.iLine].SetLineText(Lines[CursorPlace.iLine].LineText.Remove(CursorPlace.iChar, 1));
		  if (IsWrappingEnabled) CalculateLineWraps(CursorPlace.iLine, 1);
		  DispatcherQueue.TryEnqueue(() =>
		  {
			CanvasText.Invalidate();
			CanvasSelection.Invalidate();
			CanvasBeam.Invalidate();
			CanvasScrollbarMarkers.Invalidate();
			CanvasLineInfo.Invalidate();
		  });
		  UpdateText();
		 }
		}
		else
		{
		 TextAction_Delete(Selection);
		 Selection = new(Selection.VisualStart);
		}
		break;

	  case VirtualKey.Back:
		if (!IsSelection)
		{
		 if (CursorPlace.iChar == 0 && CursorPlace.iLine == 0)
		  break;
		 if (CursorPlace.iChar == 0 && CursorPlace.iLine > 0)
		 {
		  if (EditActionHistory.Count > 0)
		  {
			EditAction last = EditActionHistory.Last();
			if (last.EditActionType == EditActionType.Remove)
			{
			 // append actual newline char
			 last.TextInvolved += "\n";
			}
			else
			{
							// Record backspace join as operation-only; store the merge point (end of previous line) as Selection
							var mergePlace = new Place(Lines[CursorPlace.iLine - 1].Count, CursorPlace.iLine - 1);
							EditActionHistory.Add(new() { TextInvolved = "\n", EditActionType = EditActionType.Remove, Selection = new Range(mergePlace), SavedTexts = new() { Lines[CursorPlace.iLine - 1].SavedText, Lines[CursorPlace.iLine].SavedText } });
					  }
					 }
					 else
					 {
					  var mergePlace = new Place(Lines[CursorPlace.iLine - 1].Count, CursorPlace.iLine - 1);
					  EditActionHistory.Add(new() { TextInvolved = "\n", EditActionType = EditActionType.Remove, Selection = new Range(mergePlace), SavedTexts = new() { Lines[CursorPlace.iLine - 1].SavedText, Lines[CursorPlace.iLine].SavedText } });
					 }
		  storetext = Lines[CursorPlace.iLine].LineText;
		  Lines.RemoveAt(CursorPlace.iLine);
		  newplace = new(Lines[CursorPlace.iLine - 1].Count, CursorPlace.iLine - 1);
		  Lines[newplace.iLine].AddToLineText(storetext);
		  Selection = new Range(newplace);
		  RecalcLineNumbers();

		  if (IsWrappingEnabled) CalculateLineWraps(Math.Max(0, CursorPlace.iLine - 1), 2);

		  await DrawText(false, true);
		  checkUnsavedChanges();
		 }
		 else
		 {
		  if (Language.CommandTriggerCharacters.Contains(Lines[CursorPlace.iLine].LineText[CursorPlace.iChar - 1]))
		  {
			IsSuggesting = false;
		  }
		  string texttoremove = Lines[CursorPlace.iLine].LineText[CursorPlace.iChar - 1].ToString();

		  if (EditActionHistory.Count > 0)
		  {
			EditAction last = EditActionHistory.Last();
			if (last.EditActionType == EditActionType.Remove)
			{
			 // append raw removed char
			 last.TextInvolved += texttoremove;
			}
			else
			{
                 // Record backspace char removal as operation-only
                 EditActionHistory.Add(new() { TextInvolved = texttoremove, EditActionType = EditActionType.Remove, Selection = Selection });
			}
		  }
          else
          {
            // Record backspace removal; avoid expensive full snapshot
            EditActionHistory.Add(new() { TextInvolved = texttoremove, EditActionType = EditActionType.Remove, Selection = Selection });
          }

		  Lines[CursorPlace.iLine].SetLineText(Lines[CursorPlace.iLine].LineText.Remove(CursorPlace.iChar - 1, 1));

		  newplace.iChar--;
		  Selection = new Range(newplace);
		  FilterSuggestions();
		  RecalcLineNumbers();

		  // Recalculate wraps and force immediate redraw of ALL canvases so change is visible
		  if (IsWrappingEnabled) CalculateLineWraps(CursorPlace.iLine, 1);
		  UpdateText();
		  DispatcherQueue.TryEnqueue(() =>
		  {
			CanvasText.Invalidate();
			CanvasSelection.Invalidate();
			CanvasBeam.Invalidate();
			CanvasScrollbarMarkers.Invalidate();
			CanvasLineInfo.Invalidate();
		  });
		 }
		}
		else
		{
		 TextAction_Delete(Selection);
		 Selection = new(Selection.VisualStart);
		}
		break;

	  case VirtualKey.Home:

		newplace.iChar = newplace.iChar == Lines[newplace.iLine].Indents ? 0 : Lines[newplace.iLine].Indents;
		Selection = new(newplace);
		break;

	  case VirtualKey.End:

		newplace.iChar = Lines[newplace.iLine].Count;
		Selection = new(newplace);
		break;

	  // --- Up: maintain preferred column across visual moves ---
	  case VirtualKey.Up:
		if (IsSuggesting)
		{
		 if (SuggestionIndex > 0)
		  SuggestionIndex--;
		 else
		  SuggestionIndex = Suggestions.Count - 1;
		 break;
		}

		if (IsWrappingEnabled)
		{
		 try
		 {
		  // Use preferredVisualColumn when available; otherwise derive from current logical char position.
		  int visualCols = (preferredVisualColumn >= 0) ? preferredVisualColumn : GetVisualColumnsForLine(CursorPlace.iLine, Math.Max(0, iCharPosition));
		  double desiredX = Width_Left + (visualCols - iCharOffset) * CharWidth;

		  // Use current caret Y but desired X so the vertical move tries to keep the same column
		  Point caretPoint = PlaceToPoint(CursorPlace);
		  Point targetPoint = new(desiredX, caretPoint.Y - CharHeight);

		  Place dest = await PointToPlace(targetPoint);

		  // Fallback: if mapping didn't move and there is a previous logical line, move to its last subline and prefer stored logical iCharPosition
		  if (dest == CursorPlace && CursorPlace.iLine > 0)
		  {
			var linesSnap = SnapshotSafe(Lines);
			var prevLine = linesSnap[CursorPlace.iLine - 1];
			if (prevLine != null)
			{
			 int destChar = Math.Min(prevLine.Count, Math.Max(0, iCharPosition));
			 dest = new(destChar, CursorPlace.iLine - 1);
			}
		  }

		  if (shiftdown)
			Selection = new(Selection.Start, dest);
		  else
			Selection = new(dest);

		  // do NOT overwrite preferredVisualColumn or iCharPosition here (preserve across empty/short lines)
		 }
		 catch { /* swallow to keep behavior stable */ }
		}
		else
		{
		 // original behavior for non-wrapping mode
		 if (CursorPlace.iLine > 0)
		 {
		  newplace.iLine--;
		  newplace.iChar = Math.Min(Lines[newplace.iLine].Count, Math.Max(newplace.iChar, iCharPosition));
		  if (shiftdown)
		  {
			Selection = new(Selection.Start, newplace);
		  }
		  else
		  {
			Selection = new(newplace);
		  }
		  // DO NOT update iCharPosition or preferredVisualColumn here to avoid losing the saved column on empty lines
		 }
		}
		break;

	  // --- Down: maintain preferred column across visual moves ---
	  case VirtualKey.Down:
		if (IsSuggesting)
		{
		 if (SuggestionIndex < Suggestions.Count - 1)
		  SuggestionIndex++;
		 else
		  SuggestionIndex = 0;
		 break;
		}

		if (IsWrappingEnabled)
		{
		 try
		 {
		  // Use preferredVisualColumn when available; otherwise derive from current logical char position.
		  int visualCols = (preferredVisualColumn >= 0) ? preferredVisualColumn : GetVisualColumnsForLine(CursorPlace.iLine, Math.Max(0, iCharPosition));
		  double desiredX = Width_Left + (visualCols - iCharOffset) * CharWidth;

		  Point caretPoint = PlaceToPoint(CursorPlace);
		  Point targetPoint = new(desiredX, caretPoint.Y + CharHeight);

		  Place dest = await PointToPlace(targetPoint);

		  // Fallback: if mapping didn't move and there is a next logical line, move to its first subline and prefer stored logical iCharPosition
		  if (dest == CursorPlace && CursorPlace.iLine < Lines.Count - 1)
		  {
			var linesSnap = SnapshotSafe(Lines);
			var nextLine = linesSnap[CursorPlace.iLine + 1];
			if (nextLine != null)
			{
			 int destChar = Math.Min(nextLine.Count, Math.Max(0, iCharPosition));
			 dest = new(destChar, CursorPlace.iLine + 1);
			}
		  }

		  if (shiftdown)
			Selection = new(Selection.Start, dest);
		  else
			Selection = new(dest);

		  // do NOT overwrite preferredVisualColumn or iCharPosition here (preserve across empty/short lines)
		 }
		 catch { /* swallow to keep behavior stable */ }
		}
		else
		{
		 // original behavior for non-wrapping mode
		 if (CursorPlace.iLine < Lines.Count - 1)
		 {
		  newplace.iLine++;
		  newplace.iChar = Math.Min(Lines[newplace.iLine].Count, Math.Max(newplace.iChar, iCharPosition));
		  if (shiftdown)
		  {
			Selection = new(Selection.Start, newplace);
		  }
		  else
		  {
			Selection = new(newplace);
		  }
		  // DO NOT update iCharPosition or preferredVisualColumn here to avoid losing the saved column on empty lines
		 }
		}
		break;

	  case VirtualKey.PageDown:
		newplace.iLine = Math.Min(Lines.Count - 1, CursorPlace.iLine + iVisibleLines);
		newplace.iChar = Math.Min(Lines[newplace.iLine].Count, Math.Max(newplace.iChar, iCharPosition));
		VerticalScroll.Value += (newplace.iLine - CursorPlace.iLine) * CharHeight;
		if (shiftdown)
		{
		 Selection = new(Selection.Start, newplace);
		}
		else
		{
		 Selection = new(newplace);
		}
		break;

	  case VirtualKey.PageUp:
		newplace.iLine = Math.Max(0, CursorPlace.iLine - iVisibleLines);
		newplace.iChar = Math.Min(Lines[newplace.iLine].Count, Math.Max(newplace.iChar, iCharPosition));
		VerticalScroll.Value -= (newplace.iLine - CursorPlace.iLine) * CharHeight;
		if (shiftdown)
		{
		 Selection = new(Selection.Start, newplace);
		}
		else
		{
		 Selection = new(newplace);
		}
		break;

	  case VirtualKey.Left:
		IsSuggesting = false;
		if (CursorPlace.iChar > 0)
		{
		 newplace = new(CursorPlace);
		 newplace.iChar--;
		}
		else if (CursorPlace.iLine > 0)
		{
		 newplace = new(CursorPlace);
		 newplace.iLine--;
		 newplace.iChar = Lines[newplace.iLine].Count;
		}

		if (shiftdown)
		{
		 Selection = new(Selection.Start, newplace);

		}
		else
		{
		 Selection = new(newplace);
		}
		// update preferred state from resulting CursorPlace
		iCharPosition = CursorPlace.iChar;
		preferredVisualColumn = GetVisualColumnsForLine(CursorPlace.iLine, CursorPlace.iChar);
		break;

	  case VirtualKey.Right:
		IsSuggesting = false;
		if (CursorPlace.iChar < Lines[CursorPlace.iLine].Count)
		{
		 newplace = new(CursorPlace.iChar, CursorPlace.iLine);
		 newplace.iChar++;
		}
		else if (CursorPlace.iLine < Lines.Count - 1)
		{

		 newplace.iLine++;
		 newplace.iChar = 0;
		}

		if (shiftdown)
		{
		 Selection = new(Selection.Start, newplace);
		}
		else
		{
		 Selection = new(newplace);
		}
		// update preferred state from resulting CursorPlace
		iCharPosition = CursorPlace.iChar;
		preferredVisualColumn = GetVisualColumnsForLine(CursorPlace.iLine, CursorPlace.iChar);
		break;
	 }
	}
  }
  catch (Exception ex)
  {
	ErrorOccured?.Invoke(this, new ErrorEventArgs(ex));
  }
 }

 private async void checkUnsavedChanges()
 {
  HasUnsavedChanges = await Task.Run(() => Lines?.ToArray().Any(x => x.IsUnsaved) ?? false);
 }

 private void Scroll_KeyUp(object sender, KeyRoutedEventArgs e)
 {
  switch (e.Key)
  {
	case VirtualKey.Control:
	 controlKeyState = CoreVirtualKeyStates.None;
	 break;

	case VirtualKey.Shift:
	 shiftKeyState = CoreVirtualKeyStates.None;
	 break;
  }
 }
 #region Bindable

 private Dictionary<string, object> _properties = new Dictionary<string, object>();

 public event PropertyChangedEventHandler PropertyChanged;

 protected T Get<T>(T defaultVal = default, [CallerMemberName] string name = null)
 {
  if (!_properties.TryGetValue(name, out object value))
  {
	value = _properties[name] = defaultVal;
  }
  return (T)value;
 }

 protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
 {
  PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
 }

 protected void Set<T>(T value, [CallerMemberName] string name = null)
 {
  if (Equals(value, Get<T>(value, name)))
	return;
  _properties[name] = value;
  OnPropertyChanged(name);
 }

 #endregion Bindable

 private async Task textChanged()
 {
  try
  {
	if (Lines?.Count >= CursorPlace.iLine + 1)
	 if (Lines[CursorPlace.iLine].LineText.Length > maxchars)
	 {
	  maxchars = Lines[CursorPlace.iLine].LineText.Length;
	  HorizontalScroll.Maximum = (maxchars + 2 + Lines[CursorPlace.iLine].Indents * TabLength) * CharWidth - Scroll.ActualWidth + Width_Left;
	  VerticalScroll.Visibility = Lines.Count * CharHeight > TextControl.ActualHeight ? Visibility.Visible : Visibility.Collapsed;
	  HorizontalScroll.Visibility = maxchars * CharHeight > TextControl.ActualWidth ? Visibility.Visible : Visibility.Collapsed;
	 }
	Language lang = Language;
	await Task.Run(() => { RecalcLineNumbers(); updateFoldingPairs(lang); });
	CanvasLineInfo.Invalidate();
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new(ex)); }
 }

 protected void UpdateText()
 {
  _pendingTextUpdate = true;
  try { checkUnsavedChanges(); TextChangedTimer?.Stop(); TextChangedTimer?.Start(); } catch { }
 }
 private async Task ForceUpdateTextAsync()
 {
  if (_textUpdateRunning) return;
  _textUpdateRunning = true;
  try
  {
	string[] snapshot = await Task.Run(() => Lines?.Select(x => x.LineText).ToArray() ?? Array.Empty<string>());
	string text = await Task.Run(() => string.Join("\r\n", snapshot));
	IsSettingValue = true;
	TextChangedTimerLastText = Text;
	Text = text;
	TextChangedTimer?.Stop();
	TextChangedTimer?.Start();
	TextChanged?.Invoke(this, new(nameof(Text)));
	IsSettingValue = false;
	_pendingTextUpdate = false;
  }
  finally { _textUpdateRunning = false; }
 }

 private async Task TextChangedTimer_TickAsync()
 {
  try
  {
	TextChangedTimer?.Stop();
	if (_textUpdateRunning) { _pendingTextUpdate = true; return; }
	if (!_pendingTextUpdate) return;
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new ErrorEventArgs(ex)); }
  finally { if (_pendingTextUpdate) TextChangedTimer?.Start(); }
 }

 private void UserControl_GotFocus(object sender, RoutedEventArgs e) => IsFocused = true;
 private void UserControl_LostFocus(object sender, RoutedEventArgs e) => IsFocused = false;

 private void VerticalScroll_PointerEntered(object sender, PointerRoutedEventArgs e) => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);

 private static List<Char> LineToCharList(Line line)
 {
  var list = new List<Char>(line.Count);
  for (int i = 0; i < line.Count; i++) list.Add(line[i]);
  return list;
 }

 private T[] SnapshotSafe<T>(IEnumerable<T> source)
 {
  if (source == null) return Array.Empty<T>();
  try { return source.ToArray(); }
  catch
  {
	try { var tmp = new List<T>(); foreach (var it in source) tmp.Add(it); return tmp.ToArray(); }
	catch { return Array.Empty<T>(); }
  }
 }
}