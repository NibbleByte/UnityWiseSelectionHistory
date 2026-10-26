using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
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

			// Closing a scene destroys its objects, and reopening it creates new ones, so entries
			// pointing at them can't recover. Clean them up whenever scenes change.
			EditorSceneManager.sceneClosed += _ => ScheduleRemoveMissing();
			EditorSceneManager.sceneOpened += (_, __) => ScheduleRemoveMissing();
			EditorSceneManager.newSceneCreated += (_, __, ___) => ScheduleRemoveMissing();

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

		internal static void RemoveEntry(int index)
		{
			var state = instance;
			if (index < 0 || index >= state.m_History.Count)
				return;

			state.m_History.RemoveAt(index);
			if (index < state.m_Index || state.m_Index >= state.m_History.Count) {
				state.m_Index--;
			}

			HistoryChanged?.Invoke();
		}

		/// <summary>
		/// Removes entries holding the same objects as a newer entry, so only the newest copy of each stays.
		/// If the current entry is removed, its newer copy becomes current.
		/// </summary>
		internal static void RemoveDuplicates()
		{
			var state = instance;
			var history = state.m_History;
			var kept = new List<Entry>();
			var keptSets = new List<HashSet<Object>>();
			Entry current = state.m_Index >= 0 ? history[state.m_Index] : null;

			for (int i = history.Count - 1; i >= 0; i--) {
				var set = new HashSet<Object>(history[i].objects.Where(o => o != null));
				int existing = keptSets.FindIndex(s => s.SetEquals(set));
				if (existing >= 0) {
					if (history[i] == current) {
						current = kept[existing];
					}
					continue;
				}

				kept.Add(history[i]);
				keptSets.Add(set);
			}

			kept.Reverse();
			history.Clear();
			history.AddRange(kept);
			state.m_Index = history.IndexOf(current);

			HistoryChanged?.Invoke();
		}

		/// <summary>
		/// Removes entries whose objects were all destroyed (shown as "(missing)").
		/// If the current entry is removed, the closest older entry becomes current (or the closest newer one).
		/// </summary>
		internal static void RemoveMissing()
		{
			var state = instance;
			var history = state.m_History;

			// Count the surviving entries up to and including the current one; the last of them is the new current.
			int survivorsUpToCurrent = 0;
			for (int i = 0; i <= state.m_Index && i < history.Count; i++) {
				if (!IsMissing(history[i])) {
					survivorsUpToCurrent++;
				}
			}

			history.RemoveAll(IsMissing);
			state.m_Index = history.Count == 0 ? -1 : Mathf.Max(survivorsUpToCurrent - 1, 0);

			HistoryChanged?.Invoke();
		}

		static bool IsMissing(Entry entry) => entry.objects.All(o => o == null);

		static bool s_RemoveMissingScheduled;

		/// <summary>
		/// Runs <see cref="RemoveMissing"/> once after the current editor update. Opening a scene fires
		/// several events (closed, opened, ...) and the objects are only gone after they're done.
		/// </summary>
		static void ScheduleRemoveMissing()
		{
			if (s_RemoveMissingScheduled)
				return;

			s_RemoveMissingScheduled = true;
			EditorApplication.delayCall += () => {
				s_RemoveMissingScheduled = false;
				RemoveMissing();
			};
		}

		internal static void ClearHistory()
		{
			instance.m_History.Clear();
			instance.m_Index = -1;
			HistoryChanged?.Invoke();
		}

		/// <summary>Moves a history entry to a new position. The current entry stays current wherever it ends up.</summary>
		internal static void MoveEntry(int from, int to)
		{
			var state = instance;
			var history = state.m_History;
			if (from == to || from < 0 || from >= history.Count || to < 0 || to >= history.Count)
				return;

			var entry = history[from];
			history.RemoveAt(from);
			history.Insert(to, entry);

			if (state.m_Index == from) {
				state.m_Index = to;
			} else if (from < state.m_Index && to >= state.m_Index) {
				state.m_Index--;
			} else if (from > state.m_Index && to <= state.m_Index) {
				state.m_Index++;
			}

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