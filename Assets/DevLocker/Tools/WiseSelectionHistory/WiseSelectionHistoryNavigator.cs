using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
#if UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif

namespace DevLocker.Tools
{
	/// <summary>
	/// Records selection history in the editor and lets the mouse Back / Forward
	/// buttons (XButton1 / XButton2) step through it, like a web browser.
	///
	/// The history lives in this ScriptableSingleton's instance, to survive assembly reloads.
	/// </summary>
	[InitializeOnLoad]
	public class WiseSelectionHistoryNavigator : ScriptableSingleton<WiseSelectionHistoryNavigator>
	{
		[Serializable]
		class Entry
		{
			public Object[] objects;
		}

		const int MaxHistory = 100;

		[SerializeField] List<Entry> m_History = new List<Entry>();
		[SerializeField] int m_Index = -1;

		static bool s_IsNavigating;

		/// <summary>Raised whenever the history or the current index changes.</summary>
		internal static event Action HistoryChanged;

		/// <summary>Raised when a new selection is appended to the history (not on Back / Forward).</summary>
		internal static event Action EntryAdded;

		internal static int HistoryCount => instance.m_History.Count;
		internal static int CurrentIndex => instance.m_Index;
		internal static Object[] GetHistoryEntry(int index) => instance.m_History[index].objects;

		static WiseSelectionHistoryNavigator()
		{
			Selection.selectionChanged += OnSelectionChanged;

			// Record the current selection if it changed during the reload. Deferred because this
			// static constructor can run while Unity is restoring the instance, and touching
			// `instance` at that point would create a second, empty one.
			EditorApplication.delayCall += OnSelectionChanged;

#if UNITY_EDITOR_WIN
			EditorApplication.update += PollMouseButtons;
#else
		// Non-Windows fallback: catch the IMGUI mouse events in the most common windows.
		SceneView.duringSceneGui += _ => HandleEvent(Event.current);
		EditorApplication.hierarchyWindowItemOnGUI += (_, __) => HandleEvent(Event.current);
		EditorApplication.projectWindowItemOnGUI += (_, __) => HandleEvent(Event.current);
#endif
		}

		static void OnSelectionChanged()
		{
			if (s_IsNavigating)
				return;

			var current = Selection.objects;
			if (current == null || current.Length == 0)
				return;

			var state = instance;
			var history = state.m_History;

			if (state.m_Index >= 0) {
				var entry = history[state.m_Index];
				if (entry.objects.SequenceEqual(current))
					return;

				// Growing or shrinking the current selection (e.g. Ctrl+Click) updates the current entry instead of adding one.
				// Shrinking down to a single object counts as a new selection, so a plain click on one of
				// several selected objects doesn't overwrite the multi-object entry.
				var previous = entry.objects.Where(o => o != null).ToArray();
				var selected = new HashSet<Object>(current);
				bool grown = previous.All(selected.Contains);
				bool shrunk = current.Length > 1 && current.All(previous.Contains);
				if (grown || shrunk) {
					entry.objects = current;
					HistoryChanged?.Invoke();
					return;
				}
			}

			// A new selection discards any "forward" entries, just like a browser.
			if (state.m_Index < history.Count - 1) {
				history.RemoveRange(state.m_Index + 1, history.Count - state.m_Index - 1);
			}

			history.Add(new Entry { objects = current });
			if (history.Count > MaxHistory) {
				history.RemoveAt(0);
			}

			state.m_Index = history.Count - 1;
			EntryAdded?.Invoke();
			HistoryChanged?.Invoke();
		}

		[MenuItem("Edit/Selection/Back %[", priority = 300)]
		public static void Back() => Navigate(-1);

		[MenuItem("Edit/Selection/Forward %]", priority = 301)]
		public static void Forward() => Navigate(+1);

		internal static bool CanGoBack => instance.m_Index > 0;
		internal static bool CanGoForward => instance.m_Index < instance.m_History.Count - 1;

		static void Navigate(int direction)
		{
			int i = instance.m_Index + direction;

			// Skip entries whose objects have all been destroyed (deleted, scene unloaded, ...).
			// A dead entry is removed, so going forward the next candidate shifts into slot i.
			while (i >= 0 && i < instance.m_History.Count) {
				if (TrySelect(i))
					break;
				if (direction < 0)
					i--;
			}

			HistoryChanged?.Invoke();
		}

		/// <summary>Jumps straight to a history entry, as if pressing Back / Forward the needed number of times.</summary>
		internal static void SelectIndex(int index)
		{
			if (index >= 0 && index < instance.m_History.Count)
				TrySelect(index);

			HistoryChanged?.Invoke();
		}

		/// <summary>
		/// Selects the entry at <paramref name="i"/> and makes it current. If none of its objects
		/// exist any more, removes the entry instead and returns false.
		/// </summary>
		static bool TrySelect(int i)
		{
			var state = instance;
			var history = state.m_History;

			var alive = history[i].objects.Where(o => o != null).ToArray();
			if (alive.Length == 0) {
				history.RemoveAt(i);
				if (i <= state.m_Index)
					state.m_Index--;
				return false;
			}

			history[i].objects = alive;
			state.m_Index = i;

			s_IsNavigating = true;
			try { Selection.objects = alive; } finally { s_IsNavigating = false; }

			if (alive.Length == 1 && alive[0] is GameObject go && go.scene.IsValid())
				EditorGUIUtility.PingObject(go);
			return true;
		}

#if UNITY_EDITOR_WIN
		[DllImport("user32.dll")]
		static extern short GetAsyncKeyState(int vKey);

		const int VK_XBUTTON1 = 0x05; // Back
		const int VK_XBUTTON2 = 0x06; // Forward

		static bool s_BackWasDown;
		static bool s_ForwardWasDown;

		// Polling works in every editor window (Hierarchy, Inspector, Project, Scene, ...),
		// whereas IMGUI events only reach the window under the cursor that chooses to forward them.
		static void PollMouseButtons()
		{
			bool backDown = (GetAsyncKeyState(VK_XBUTTON1) & 0x8000) != 0;
			bool forwardDown = (GetAsyncKeyState(VK_XBUTTON2) & 0x8000) != 0;

			// Only react while Unity is the focused application.
			if (UnityEditorInternal.InternalEditorUtility.isApplicationActive) {
				if (backDown && !s_BackWasDown) Back();
				if (forwardDown && !s_ForwardWasDown) Forward();
			}

			s_BackWasDown = backDown;
			s_ForwardWasDown = forwardDown;
		}
#else
	static void HandleEvent(Event e)
	{
		if (e == null || e.type != EventType.MouseDown)
			return;

		if (e.button == 3) { Back(); e.Use(); }
		else if (e.button == 4) { Forward(); e.Use(); }
	}
#endif
	}
}