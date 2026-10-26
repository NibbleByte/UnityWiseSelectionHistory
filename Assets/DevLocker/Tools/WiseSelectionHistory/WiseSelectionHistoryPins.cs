using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DevLocker.Tools
{
	/// <summary>
	/// Pinned entries for <see cref="WiseSelectionHistoryWindow"/>.
	///
	/// Saved in UserSettings. Objects are stored as GlobalObjectIds,
	/// which also work for scene objects (they resolve once their scene is loaded).
	/// </summary>
	[FilePath("UserSettings/WiseSelectionHistoryPins.asset", FilePathAttribute.Location.ProjectFolder)]
	public class WiseSelectionHistoryPins : ScriptableSingleton<WiseSelectionHistoryPins>
	{
		[Serializable]
		class Pin
		{
			public string[] globalIds;
			public string[] names;    // object names at pin time
			public string typeName;   // first object's type, for the icon of scene objects
			public bool isSceneObject; // first object is a scene object

			[NonSerialized] public GUIContent content;
		}

		[SerializeField] List<Pin> m_Pins = new List<Pin>();

		internal static event Action PinsChanged;

		internal static int Count => instance.m_Pins.Count;

		internal static bool IsSceneObject(int index) => instance.m_Pins[index].isSceneObject;

		internal static string[] GetNames(int index) => instance.m_Pins[index].names ?? Array.Empty<string>();

		internal static string GetName(int index)
		{
			var names = GetNames(index);
			if (names.Length == 0)
				return "(unknown)";

			return names.Length > 1 ? $"{names[0]} [{names.Length}]" : names[0];
		}

		/// <summary>Button content built from the stored info, without loading the objects.</summary>
		internal static GUIContent GetContent(int index)
		{
			var pin = instance.m_Pins[index];
			if (pin.content == null) {
				pin.content = new GUIContent(GetName(index), GetIcon(pin), string.Join("\n", GetNames(index)));
			}
			return pin.content;
		}

		private static Texture GetIcon(Pin pin)
		{
			// Assets have their icon cached by the AssetDatabase; scene objects get their type's icon.
			string path = GetAssetPaths(pin).FirstOrDefault();
			if (!string.IsNullOrEmpty(path))
				return AssetDatabase.GetCachedIcon(path);

			var type = string.IsNullOrEmpty(pin.typeName) ? null : Type.GetType(pin.typeName);
			return type != null ? EditorGUIUtility.ObjectContent(null, type).image : null;
		}

		/// <summary>Project-relative paths of the pinned assets (scene objects are skipped), without loading them.</summary>
		internal static string[] GetAssetPaths(int index) => GetAssetPaths(instance.m_Pins[index]);

		static string[] GetAssetPaths(Pin pin) => ParseIds(pin)
			.Where(id => id.identifierType == 1 || id.identifierType == 3) // imported asset / source asset
			.Select(id => AssetDatabase.GUIDToAssetPath(id.assetGUID))
			.Where(path => !string.IsNullOrEmpty(path))
			.Distinct()
			.ToArray();

		/// <summary>
		/// Loads the pinned objects with a single batch call (much faster than one call per object, per Unity docs).
		/// Objects that no longer exist, or whose scene isn't open, are left out and counted in <paramref name="missingCount"/>.
		/// </summary>
		internal static Object[] Resolve(int index, out int missingCount)
		{
			var ids = ParseIds(instance.m_Pins[index]);
			var objects = new Object[ids.Length];
			GlobalObjectId.GlobalObjectIdentifiersToObjectsSlow(ids, objects);

			var alive = objects.Where(o => o != null).ToArray();
			missingCount = instance.m_Pins[index].globalIds.Length - alive.Length;
			return alive;
		}

		/// <summary>Index of the pin holding exactly these objects, or -1.</summary>
		internal static int IndexOf(Object[] objects)
		{
			var ids = ToGlobalIds(objects);
			return instance.m_Pins.FindIndex(pin => new HashSet<string>(pin.globalIds).SetEquals(ids));
		}

		internal static void Add(Object[] objects)
		{
			objects = objects.Where(o => o != null).ToArray();
			if (objects.Length == 0 || IndexOf(objects) >= 0)
				return;

			instance.m_Pins.Add(new Pin {
				globalIds = ToGlobalIds(objects),
				names = objects.Select(o => o.name).ToArray(),
				typeName = objects[0].GetType().AssemblyQualifiedName,
				isSceneObject = !EditorUtility.IsPersistent(objects[0]),
			});
			SaveAndNotify();
		}

		internal static void RemoveAt(int index)
		{
			instance.m_Pins.RemoveAt(index);
			SaveAndNotify();
		}

		internal static void Move(int from, int to)
		{
			var pins = instance.m_Pins;
			if (from == to || from < 0 || from >= pins.Count || to < 0 || to >= pins.Count)
				return;

			var pin = pins[from];
			pins.RemoveAt(from);
			pins.Insert(to, pin);
			SaveAndNotify();
		}

		internal static void Clear()
		{
			instance.m_Pins.Clear();
			SaveAndNotify();
		}

		static GlobalObjectId[] ParseIds(Pin pin) => pin.globalIds
			.Select(idString => GlobalObjectId.TryParse(idString, out var id) ? id : default)
			.ToArray();

		static string[] ToGlobalIds(Object[] objects)
		{
			objects = objects.Where(o => o != null).ToArray();
			var ids = new GlobalObjectId[objects.Length];
			GlobalObjectId.GetGlobalObjectIdsSlow(objects, ids);
			return ids.Select(id => id.ToString()).ToArray();
		}

		static void SaveAndNotify()
		{
			instance.Save(true);
			PinsChanged?.Invoke();
		}
	}
}
