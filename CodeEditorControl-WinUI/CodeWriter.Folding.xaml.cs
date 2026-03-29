using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeEditorControl_WinUI;

public partial class CodeWriter : UserControl, INotifyPropertyChanged
{
 List<Folding> foldings = [];
 private readonly object _foldingsLock = new();

 private void updateFoldingPairs(Language languange)
 {
  try
  {
	var newFoldings = new List<Folding>();
	if (languange.FoldingPairs != null)
	{
	 foreach (var line in SnapshotSafe(Lines))
	 {
	  if (line?.Language?.FoldingPairs == null) continue;
	  foreach (var sf in line.Language.FoldingPairs)
	  {
		foreach (Match match in Regex.Matches(line.LineText ?? string.Empty, sf.RegexStart))
		{
		 if (!match.Success) continue;
		 string candidate = match.Value;
		 if (sf.MatchingGroup > 0 && match.Groups.Count > sf.MatchingGroup) candidate = match.Groups[sf.MatchingGroup].Value;
		 if (sf.FoldingIgnoreWords?.Contains(candidate) == true) continue;
		 newFoldings.Add(new Folding { Name = candidate, StartLine = line.iLine, Endline = -1 });
		}
		foreach (Match match in Regex.Matches(line.LineText ?? string.Empty, sf.RegexEnd))
		{
		 if (!match.Success) continue;
		 string key = match.Value;
		 if (sf.MatchingGroup > 0 && match.Groups.Count > sf.MatchingGroup) key = match.Groups[sf.MatchingGroup].Value;
		 if (sf.FoldingIgnoreWords?.Contains(key) == true) continue;
		 Folding mf = null;
		 for (int i = newFoldings.Count - 1; i >= 0; i--)
		 {
		  var f = newFoldings[i];
		  if (f != null && f.Endline == -1 && (sf.MatchingGroup <= 0 || f.Name == key)) { mf = f; break; }
		 }
		 if (mf != null) mf.Endline = line.iLine;
		}
	  }
	 }
	 newFoldings.RemoveAll(x => x.Endline == -1);
	}
	lock (_foldingsLock) { foldings = newFoldings; }
  }
  catch (Exception ex) { ErrorOccured?.Invoke(this, new(ex)); }
 }
}