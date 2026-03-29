using System;
using System.Windows.Input;

namespace CodeEditorControl_WinUI;
/// <summary>Represents a character position (line + column) in the editor.</summary>
public class Place : IEquatable<Place>
{
	public int iChar = 0;
	public int iLine = 0;
	public Place() { }
	/// <summary>Creates a copy of <paramref name="other"/>.</summary>
	public Place(Place other) { iChar = other.iChar; iLine = other.iLine; }
	/// <summary>Creates a place at the given character and line index.</summary>
	public Place(int iChar, int iLine) { this.iChar = iChar; this.iLine = iLine; }
	public static Place Empty => new();
	public static bool operator !=(Place p1, Place p2) => !p1.Equals(p2);
	public static Place operator +(Place p1, Place p2) => new(p1.iChar + p2.iChar, p1.iLine + p2.iLine);
	public static Place operator +(Place p1, int c) => new(p1.iChar + c, p1.iLine);
	public static Place operator -(Place p1, int c) => new(p1.iChar - c, p1.iLine);
	public static bool operator <(Place p1, Place p2) => p1.iLine < p2.iLine || (p1.iLine == p2.iLine && p1.iChar < p2.iChar);
	public static bool operator <=(Place p1, Place p2) => p1.Equals(p2) || p1 < p2;
	public static bool operator ==(Place p1, Place p2) => p1.Equals(p2);
	public static bool operator >(Place p1, Place p2) => p1.iLine > p2.iLine || (p1.iLine == p2.iLine && p1.iChar > p2.iChar);
	public static bool operator >=(Place p1, Place p2) => p1.Equals(p2) || p1 > p2;
	public bool Equals(Place other) => iChar == other.iChar && iLine == other.iLine;
	public override bool Equals(object obj) => obj is Place p && Equals(p);
	public override int GetHashCode() => iChar.GetHashCode() ^ iLine.GetHashCode();
	/// <summary>Offsets position by <paramref name="dx"/> chars and <paramref name="dy"/> lines.</summary>
	public void Offset(int dx, int dy) { iChar += dx; iLine += dy; }
	public override string ToString() => $"({iLine + 1},{iChar + 1})";
}
/// <summary>Simple relay command implementation.</summary>
public class RelayCommand : ICommand
{
	private readonly Action _execute;
	private readonly Func<bool> _canExecute;
	public event EventHandler CanExecuteChanged;
	public RelayCommand(Action execute) : this(execute, null) { }
	public RelayCommand(Action execute, Func<bool> canExecute) { _execute = execute ?? throw new ArgumentNullException(nameof(execute)); _canExecute = canExecute; }
	public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
	public void Execute(object parameter) => _execute();
	/// <summary>Raises <see cref="CanExecuteChanged"/>.</summary>
	public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
/// <summary>Represents a text selection range between two places.</summary>
public class Range : Bindable
{
	public Range() { }
	/// <summary>Creates a range from a copy of <paramref name="range"/>.</summary>
	public Range(Range range) { Start = range.Start; End = range.End; }
	/// <summary>Creates a zero-length range at <paramref name="place"/>.</summary>
	public Range(Place place) { Start = place ?? new(); End = place ?? new(); }
	/// <summary>Creates a range from <paramref name="start"/> to <paramref name="end"/>.</summary>
	public Range(Place start, Place end) { Start = start; End = end; }
	public Place Start { get => Get(new Place()); set => Set(value); }
	public Place End { get => Get(new Place()); set => Set(value); }
	public Place VisualStart => End > Start ? new(Start) : new(End);
	public Place VisualEnd => End > Start ? new(End) : new(Start);
	public static Range operator +(Range r, int c) => new(r.Start + c, r.End + c);
	public static Range operator -(Range r, int c) => new(r.Start - c, r.End - c);
	public override string ToString() => $"{Start} -> {End}";
}
/// <summary>Represents a code folding region.</summary>
public class Folding : Bindable
{
	public string Name { get => Get<string>(null); set => Set(value); }
	public int StartLine { get => Get(-1); set => Set(value); }
	public int Endline { get => Get(-1); set => Set(value); }
}
/// <summary>Represents a highlighted text range.</summary>
public class HighlightRange
{
	public Place Start { get; set; }
	public Place End { get; set; }
}
