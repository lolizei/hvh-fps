using System;

namespace HvH.Mods;

public enum ModSettingKind
{
	Toggle,
	Slider,
	Choice,
}

/// <summary>
/// A description of one tweakable value on a feature: what to call it, what
/// kind of control it needs, and how to read and write it.
///
/// This is what lets a menu it has never seen before render a feature it has
/// never seen before. Without it, every menu would need hard-coded knowledge of
/// every feature, and "write your own menu" would be a lie.
/// </summary>
public sealed class ModSetting
{
	public string Label { get; init; }
	public ModSettingKind Kind { get; init; }

	/// <summary>Slider bounds. Ignored for other kinds.</summary>
	public float Min { get; init; }
	public float Max { get; init; } = 1f;
	public float Step { get; init; } = 0.01f;

	/// <summary>Option labels for <see cref="ModSettingKind.Choice"/>.</summary>
	public string[] Choices { get; init; } = Array.Empty<string>();

	public Func<bool> GetBool { get; init; }
	public Action<bool> SetBool { get; init; }

	public Func<float> GetFloat { get; init; }
	public Action<float> SetFloat { get; init; }

	public Func<int> GetInt { get; init; }
	public Action<int> SetInt { get; init; }

	public static ModSetting Toggle( string label, Func<bool> get, Action<bool> set ) => new()
	{
		Label = label,
		Kind = ModSettingKind.Toggle,
		GetBool = get,
		SetBool = set,
	};

	public static ModSetting Slider( string label, Func<float> get, Action<float> set,
		float min, float max, float step = 0.01f ) => new()
	{
		Label = label,
		Kind = ModSettingKind.Slider,
		GetFloat = get,
		SetFloat = set,
		Min = min,
		Max = max,
		Step = step,
	};

	public static ModSetting Choice( string label, Func<int> get, Action<int> set,
		params string[] choices ) => new()
	{
		Label = label,
		Kind = ModSettingKind.Choice,
		GetInt = get,
		SetInt = set,
		Choices = choices,
	};

	/// <summary>Current value rendered for display, whatever the kind.</summary>
	public string DisplayValue => Kind switch
	{
		ModSettingKind.Toggle => ( GetBool?.Invoke() ?? false ) ? "ON" : "OFF",
		ModSettingKind.Slider => ( GetFloat?.Invoke() ?? 0f ).ToString( Step < 1f ? "0.00" : "0" ),
		ModSettingKind.Choice => ChoiceLabel(),
		_ => "",
	};

	private string ChoiceLabel()
	{
		var index = GetInt?.Invoke() ?? 0;

		return index >= 0 && index < Choices.Length ? Choices[index] : "";
	}
}
