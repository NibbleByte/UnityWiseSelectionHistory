using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.Tools
{
	/// <summary>
	/// Shows the history recorded by <see cref="WiseSelectionHistoryNavigator"/> as a list of buttons.
	/// </summary>
	public class WiseSelectionHistoryWindow : EditorWindow
	{
		private Vector2 m_Scroll;
		private bool m_ScrollToEnd;
		private GUIStyle m_EntryStyle;
		private static readonly Color HistorySelectedTint = Color.yellow;
		private static readonly Color HistorySceneTint = new Color(0.65f, 0.85f, 1f);
		private static readonly Color PinTint = new Color(0.6f, 1f, 0.6f);
		private static readonly Color ScenePinTint = new Color(0.62f, 0.92f, 0.84f); // pin green mixed with the scene object blue

		// Dragging an entry starts a regular Unity drag of its objects, so it can be dropped anywhere
		// Unity accepts objects (scene, hierarchy, object fields, ...). Dropping it back on this window reorders.
		// The generic data marks our own drags and carries the dragged entry's index.
		private const string DragDataKey = "WiseSelectionHistoryEntryIndex";
		private const string PinDragDataKey = "WiseSelectionHistoryPinIndex";

		// Rects are from the last non-layout pass, in scroll view coordinates (pins: window coordinates).
		private readonly List<Rect> m_EntryRects = new List<Rect>();
		private readonly List<Rect> m_PinRects = new List<Rect>();
		private int m_PinDragFromIndex = -1;
		private int m_PinDropIndex = -1;
		private int m_PressedIndex = -1;    // history entry under a left press, waiting to see if it becomes a drag
		private int m_PressedPinIndex = -1; // same for pinned entries
		private Vector2 m_PressedPosition;
		private int m_DragFromIndex = -1; // set while one of our drags hovers this window
		private int m_DropIndex = -1;     // insertion slot, 0..HistoryCount

		[MenuItem("Edit/Selection/History Window", priority = 320)]
		static void Open() => GetWindow<WiseSelectionHistoryWindow>("Selection History");

		void OnEnable()
		{
			WiseSelectionHistoryNavigator.HistoryChanged += Repaint;
			WiseSelectionHistoryNavigator.EntryAdded += OnEntryAdded;
			WiseSelectionHistoryPins.PinsChanged += Repaint;
			EditorApplication.hierarchyChanged += Repaint; // renamed or deleted objects
		}

		void OnDisable()
		{
			WiseSelectionHistoryNavigator.HistoryChanged -= Repaint;
			WiseSelectionHistoryNavigator.EntryAdded -= OnEntryAdded;
			WiseSelectionHistoryPins.PinsChanged -= Repaint;
			EditorApplication.hierarchyChanged -= Repaint;
		}

		// Only new selections scroll to the end; Back / Forward and clicks leave the scroll alone.
		void OnEntryAdded() => m_ScrollToEnd = true;

		void OnGUI()
		{
			if (m_EntryStyle == null) {
				m_EntryStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };			}

			bool vertical = position.height >= position.width;

			// Forget the last pressed entry on any press, before the arrow buttons can consume it.
			// The entry under the mouse (if any) sets it again below.
			if (Event.current.type == EventType.MouseDown) {
				m_PressedIndex = -1;
				m_PressedPinIndex = -1;
			}

			var prevIconSize = EditorGUIUtility.GetIconSize();
			EditorGUIUtility.SetIconSize(new Vector2(16, 16));

			// Clicks change the history, which would break the GUILayout pass if done mid-loop,
			// so they are collected here and applied once everything is drawn.
			int clickedIndex = -1;
			int clickedPinIndex = -1;
			int navigateDirection = 0;
			int moveFrom = -1, moveTo = -1;
			int pinMoveFrom = -1, pinMoveTo = -1;

			if (vertical) {
				EditorGUILayout.BeginVertical();
			} else {
				EditorGUILayout.BeginHorizontal();
			}

			const float ArrowWidth = 28f;

			EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(false));
			using (new EditorGUI.DisabledScope(!WiseSelectionHistoryNavigator.CanGoBack)) {
				if (GUILayout.Button(EditorGUIUtility.TrIconContent("back", "Back"), GUILayout.Width(ArrowWidth))) {
					navigateDirection = -1;
				}
			}
			using (new EditorGUI.DisabledScope(!WiseSelectionHistoryNavigator.CanGoForward)) {
				if (GUILayout.Button(EditorGUIUtility.TrIconContent("forward", "Forward"), GUILayout.Width(ArrowWidth))) {
					navigateDirection = +1;
				}
			}
			EditorGUILayout.EndHorizontal();

			var evt = Event.current;
			var prevBackground = GUI.backgroundColor;
			var prevColor = GUI.color;

			var options = vertical
				? new[] { GUILayout.ExpandWidth(true) }
				: new[] { GUILayout.ExpandWidth(false), GUILayout.MaxWidth(160f) };

			// Pinned entries: always visible, outside the scroll view.
			// Tinted green to set them apart from the history.
			if (WiseSelectionHistoryPins.Count > 0) {
				if (vertical) {
					EditorGUILayout.BeginVertical();
				} else {
					EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(false));
				}

				HandlePinDrag(vertical, ref pinMoveFrom, ref pinMoveTo);

				bool recordPinRects = evt.type != EventType.Layout;
				if (recordPinRects) {
					m_PinRects.Clear();
				}

				for (int i = 0; i < WiseSelectionHistoryPins.Count; i++) {
					// Drawn from the info stored with the pin; the objects are only loaded when used.
					var content = WiseSelectionHistoryPins.GetContent(i);
					GUI.backgroundColor = WiseSelectionHistoryPins.IsSceneObject(i) ? ScenePinTint : PinTint;

					if (i == m_PinDragFromIndex) {
						GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, prevColor.a * 0.4f);
					}

					var rect = GUILayoutUtility.GetRect(content, m_EntryStyle, options);
					if (recordPinRects) {
						m_PinRects.Add(rect);
					}

					HandleContextClick(rect, -1, i);

					if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition)) {
						m_PressedPinIndex = i;
						m_PressedPosition = evt.mousePosition;
					}

					if (GUI.Button(rect, content, m_EntryStyle)) {
						clickedPinIndex = i;
					}

					GUI.backgroundColor = prevBackground;
					GUI.color = prevColor;
				}

				if (m_PinDragFromIndex >= 0 && evt.type == EventType.Repaint) {
					DrawDropMarker(m_PinRects, m_PinDropIndex, vertical);
				}

				if (vertical) {
					EditorGUILayout.EndVertical();
					GUILayout.Space(6f);
				} else {
					EditorGUILayout.EndHorizontal();
					GUILayout.Space(6f);
				}
			}

			// The scroll view clamps this to the content size, so it lands on the last entry
			// in whichever direction the list currently runs.
			if (m_ScrollToEnd)
				m_Scroll = new Vector2(float.MaxValue, float.MaxValue);

			m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
			if (vertical) {
				EditorGUILayout.BeginVertical();
			} else {
				EditorGUILayout.BeginHorizontal();
			}

			// Handled before the buttons, so they never see the drag's events.
			HandleDrag(vertical, ref moveFrom, ref moveTo);

			bool recordRects = evt.type != EventType.Layout; // layout pass only returns placeholder rects
			if (recordRects) {
				m_EntryRects.Clear();
			}

			int current = WiseSelectionHistoryNavigator.CurrentIndex;

			for (int i = 0; i < WiseSelectionHistoryNavigator.HistoryCount; i++) {
				var objects = WiseSelectionHistoryNavigator.GetHistoryEntry(i);
				var first = objects.FirstOrDefault(o => o != null);
				bool isScene = first != null && !EditorUtility.IsPersistent(first);

				if (i == current) {
					GUI.backgroundColor = HistorySelectedTint;
				} else if (isScene) {
					GUI.backgroundColor = HistorySceneTint;
				}

				if (i == m_DragFromIndex) {
					GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, prevColor.a * 0.4f);
				}

				var content = GetContent(objects, first);
				var rect = GUILayoutUtility.GetRect(content, m_EntryStyle, options);
				if (recordRects) {
					m_EntryRects.Add(rect);
				}

				HandleContextClick(rect, i, -1);

				// Remember the press, but let the button take it too, so a press without a drag is still a click.
				if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition)) {
					m_PressedIndex = i;
					m_PressedPosition = evt.mousePosition;
				}

				if (GUI.Button(rect, content, m_EntryStyle)) {
					clickedIndex = i;
				}

				GUI.backgroundColor = prevBackground;
				GUI.color = prevColor;
			}

			if (m_DragFromIndex >= 0 && evt.type == EventType.Repaint) {
				DrawDropMarker(m_EntryRects, m_DropIndex, vertical);
			}

			if (vertical) {
				EditorGUILayout.EndVertical();
			} else {
				EditorGUILayout.EndHorizontal();
			}

			EditorGUILayout.EndScrollView();

			if (vertical) {
				EditorGUILayout.EndVertical();
			} else {
				EditorGUILayout.EndHorizontal();
			}

			// The content size is only known by the Repaint pass, so keep forcing the scroll until then.
			if (Event.current.type == EventType.Repaint)
				m_ScrollToEnd = false;

			EditorGUIUtility.SetIconSize(prevIconSize);

			if (navigateDirection < 0) {
				WiseSelectionHistoryNavigator.Back();
			} else if (navigateDirection > 0) {
				WiseSelectionHistoryNavigator.Forward();
			} else if (clickedIndex >= 0) {
				WiseSelectionHistoryNavigator.SelectIndex(clickedIndex);
			} else if (clickedPinIndex >= 0) {
				SelectPin(clickedPinIndex);
			} else if (moveFrom >= 0) {
				WiseSelectionHistoryNavigator.MoveEntry(moveFrom, moveTo);
			} else if (pinMoveFrom >= 0) {
				WiseSelectionHistoryPins.Move(pinMoveFrom, pinMoveTo);
			}
		}

		/// <summary>Selects the pin's objects as a regular selection, so the navigator records it in the history as usual.</summary>
		private static void SelectPin(int pinIndex)
		{
			var objects = WiseSelectionHistoryPins.Resolve(pinIndex, out int missingCount);

			if (missingCount > 0) {
				int total = objects.Length + missingCount;
				string message = objects.Length == 0
					? $"None of the pinned objects could be found ({total}).\n\nThey were deleted, or they are scene objects whose scene isn't open."
					: $"{missingCount} of {total} pinned objects could not be found.\n\nThey were deleted, or they are scene objects whose scene isn't open. The rest will be selected.";
				EditorUtility.DisplayDialog("Selection History", message, "OK");
			}

			if (objects.Length > 0) {
				Selection.objects = objects;
			}
		}

		private void HandleDrag(bool vertical, ref int moveFrom, ref int moveTo)
		{
			var evt = Event.current;

			switch (evt.type) {
				case EventType.MouseDrag:
					if (m_PressedIndex >= 0 && m_PressedIndex < WiseSelectionHistoryNavigator.HistoryCount
						&& Vector2.Distance(evt.mousePosition, m_PressedPosition) > 6f) {
						StartEntryDrag(WiseSelectionHistoryNavigator.GetHistoryEntry(m_PressedIndex), m_PressedIndex, -1);
						m_PressedIndex = -1;
						evt.Use();
					}
					break;

				case EventType.DragUpdated:
				case EventType.DragPerform:
					if (!(DragAndDrop.GetGenericData(DragDataKey) is int from)) {
						break; // someone else's drag; not accepted here
					}

					m_DragFromIndex = from;
					m_DropIndex = GetDropIndex(m_EntryRects, evt.mousePosition, vertical);
					DragAndDrop.visualMode = DragAndDropVisualMode.Move;

					if (evt.type == EventType.DragPerform) {
						DragAndDrop.AcceptDrag();

						// Removing the entry first shifts every later slot down by one.
						int to = m_DropIndex > from ? m_DropIndex - 1 : m_DropIndex;
						if (to != from) {
							moveFrom = from;
							moveTo = to;
						}
						ClearDropState();
					}

					evt.Use();
					Repaint();
					break;

				case EventType.DragExited:
					// Left the window, dropped, or cancelled with Esc.
					ClearDropState();
					break;
			}
		}

		/// <summary>Same as <see cref="HandleDrag"/>, for the pinned entries.</summary>
		private void HandlePinDrag(bool vertical, ref int moveFrom, ref int moveTo)
		{
			var evt = Event.current;

			switch (evt.type) {
				case EventType.MouseDrag:
					if (m_PressedPinIndex >= 0 && m_PressedPinIndex < WiseSelectionHistoryPins.Count
						&& Vector2.Distance(evt.mousePosition, m_PressedPosition) > 6f) {
						// Missing objects are left out silently; a pin with none can still be reordered.
						StartEntryDrag(WiseSelectionHistoryPins.Resolve(m_PressedPinIndex, out _), -1, m_PressedPinIndex);
						m_PressedPinIndex = -1;
						evt.Use();
					}
					break;

				case EventType.DragUpdated:
				case EventType.DragPerform:
					if (!(DragAndDrop.GetGenericData(PinDragDataKey) is int from)) {
						break;
					}

					// The pins aren't in a scroll view, so only accept drops over them (with a little slack).
					if (!GetBounds(m_PinRects, 4f).Contains(evt.mousePosition)) {
						ClearPinDropState();
						break;
					}

					m_PinDragFromIndex = from;
					m_PinDropIndex = GetDropIndex(m_PinRects, evt.mousePosition, vertical);
					DragAndDrop.visualMode = DragAndDropVisualMode.Move;

					if (evt.type == EventType.DragPerform) {
						DragAndDrop.AcceptDrag();

						int to = m_PinDropIndex > from ? m_PinDropIndex - 1 : m_PinDropIndex;
						if (to != from) {
							moveFrom = from;
							moveTo = to;
						}
						ClearPinDropState();
					}

					evt.Use();
					Repaint();
					break;

				case EventType.DragExited:
					ClearPinDropState();
					break;
			}
		}

		/// <param name="historyIndex">Index of the dragged history entry, used to reorder on drop, or -1.</param>
		/// <param name="pinIndex">Index of the dragged pin, used to reorder on drop, or -1.</param>
		private void StartEntryDrag(Object[] entryObjects, int historyIndex, int pinIndex)
		{
			var objects = entryObjects.Where(o => o != null).ToArray();

			// A pin that isn't loaded has no objects, but can still be reordered.
			if (objects.Length == 0 && pinIndex < 0)
				return;

			// The pressed button still holds the mouse capture; release it so it doesn't linger as pressed.
			GUIUtility.hotControl = 0;

			DragAndDrop.PrepareStartDrag();
			DragAndDrop.objectReferences = objects;
			// Asset paths let windows that work with files (e.g. the Project window) accept the drag too.
			DragAndDrop.paths = objects
				.Select(AssetDatabase.GetAssetPath)
				.Where(path => !string.IsNullOrEmpty(path))
				.ToArray();
			if (historyIndex >= 0) {
				DragAndDrop.SetGenericData(DragDataKey, historyIndex);
			}
			if (pinIndex >= 0) {
				DragAndDrop.SetGenericData(PinDragDataKey, pinIndex);
			}

			string title = objects.Length == 0 ? WiseSelectionHistoryPins.GetName(pinIndex)
				: objects.Length > 1 ? $"{objects[0].name} [{objects.Length}]"
				: objects[0].name;
			DragAndDrop.StartDrag(title);
		}

		private void ClearPinDropState()
		{
			if (m_PinDragFromIndex < 0 && m_PinDropIndex < 0)
				return;

			m_PinDragFromIndex = -1;
			m_PinDropIndex = -1;
			Repaint();
		}

		private static Rect GetBounds(List<Rect> rects, float padding)
		{
			if (rects.Count == 0)
				return Rect.zero;

			float xMin = rects.Min(r => r.xMin), yMin = rects.Min(r => r.yMin);
			float xMax = rects.Max(r => r.xMax), yMax = rects.Max(r => r.yMax);
			return Rect.MinMaxRect(xMin - padding, yMin - padding, xMax + padding, yMax + padding);
		}

		/// <summary>
		/// Shows the entry context menu on right-click. Call before drawing the entry's button.
		/// Pass the history index for history entries, or the pin index for pinned entries (the other one -1).
		/// </summary>
		private static void HandleContextClick(Rect rect, int historyIndex, int pinIndex)
		{
			var evt = Event.current;
			if (!rect.Contains(evt.mousePosition))
				return;

			// GUI.Button reacts to the right mouse button too, so swallow the press before it sees it.
			if (evt.type == EventType.MouseDown && evt.button == 1) {
				evt.Use();
			} else if (evt.type == EventType.ContextClick) {
				evt.Use();
				ShowContextMenu(historyIndex, pinIndex);
			}
		}

		private static void ShowContextMenu(int historyIndex, int pinIndex)
		{
			bool isPin = pinIndex >= 0;

			// Pins are described by their stored info, so opening the menu doesn't load their objects.
			var objects = new Object[0];
			string[] names, paths;
			if (isPin) {
				names = WiseSelectionHistoryPins.GetNames(pinIndex);
				paths = WiseSelectionHistoryPins.GetAssetPaths(pinIndex);
			} else {
				objects = WiseSelectionHistoryNavigator.GetHistoryEntry(historyIndex).Where(o => o != null).ToArray();
				names = objects.Select(o => o.name).ToArray();
				paths = objects
					.Select(AssetDatabase.GetAssetPath)
					.Where(path => !string.IsNullOrEmpty(path))
					.ToArray();
			}

			var menu = new GenericMenu();

			int pinnedAt = isPin ? pinIndex : WiseSelectionHistoryPins.IndexOf(objects);
			if (pinnedAt >= 0) {
				menu.AddItem(new GUIContent("Unpin"), false, () => WiseSelectionHistoryPins.RemoveAt(pinnedAt));
			} else if (objects.Length > 0) {
				menu.AddItem(new GUIContent("Pin"), false, () => WiseSelectionHistoryPins.Add(objects));
			} else {
				menu.AddDisabledItem(new GUIContent("Pin"));
			}

			menu.AddSeparator("");

			if (isPin) {
				menu.AddItem(new GUIContent("Remove"), false, () => WiseSelectionHistoryPins.RemoveAt(pinIndex));
			} else {
				menu.AddItem(new GUIContent("Remove"), false, () => WiseSelectionHistoryNavigator.RemoveEntry(historyIndex));
			}
			menu.AddItem(new GUIContent("Remove All Missing"), false, WiseSelectionHistoryNavigator.RemoveMissing);
			menu.AddItem(new GUIContent("Remove All Duplicates"), false, WiseSelectionHistoryNavigator.RemoveDuplicates);
			menu.AddItem(new GUIContent("Remove All Unpinned"), false, WiseSelectionHistoryNavigator.ClearHistory);
			menu.AddItem(new GUIContent("Remove All"), false, () => {
				WiseSelectionHistoryNavigator.ClearHistory();
				WiseSelectionHistoryPins.Clear();
			});

			menu.AddSeparator("");

			if (names.Length > 0) {
				menu.AddItem(new GUIContent("Copy Names"), false, () =>
					EditorGUIUtility.systemCopyBuffer = string.Join("\n", names));
			} else {
				menu.AddDisabledItem(new GUIContent("Copy Names"));
			}

			if (paths.Length > 0) {
				menu.AddItem(new GUIContent("Copy Relative Paths"), false, () =>
					EditorGUIUtility.systemCopyBuffer = string.Join("\n", paths));
			} else {
				menu.AddDisabledItem(new GUIContent("Copy Relative Paths"));
			}

			menu.AddSeparator("");
			menu.AddItem(new GUIContent("Cancel"), false, () => { });

			menu.ShowAsContext();
		}

		private void ClearDropState()
		{
			m_DragFromIndex = -1;
			m_DropIndex = -1;
			Repaint();
		}

		/// <summary>The slot between entries closest to <paramref name="mousePosition"/>, from 0 (before the first) to the entry count (after the last).</summary>
		private static int GetDropIndex(List<Rect> rects, Vector2 mousePosition, bool vertical)
		{
			int index = 0;
			foreach (var rect in rects) {
				float center = vertical ? rect.center.y : rect.center.x;
				float mouse = vertical ? mousePosition.y : mousePosition.x;
				if (mouse > center) {
					index++;
				}
			}
			return index;
		}

		private static void DrawDropMarker(List<Rect> rects, int dropIndex, bool vertical)
		{
			if (dropIndex < 0 || rects.Count == 0)
				return;

			const float Thickness = 2f;
			bool afterLast = dropIndex >= rects.Count;
			var rect = rects[afterLast ? rects.Count - 1 : dropIndex];

			Rect marker;
			if (vertical) {
				float y = afterLast ? rect.yMax : rect.yMin;
				marker = new Rect(rect.xMin, y - Thickness * 0.5f, rect.width, Thickness);
			} else {
				float x = afterLast ? rect.xMax : rect.xMin;
				marker = new Rect(x - Thickness * 0.5f, rect.yMin, Thickness, rect.height);
			}

			EditorGUI.DrawRect(marker, new Color(0.25f, 0.6f, 1f));
		}

		static GUIContent GetContent(Object[] objects, Object first)
		{
			// Clicking a missing entry removes it from the history.
			if (first == null)
				return new GUIContent("(missing)", "All objects in this entry were destroyed.");

			var content = new GUIContent(EditorGUIUtility.ObjectContent(first, first.GetType()));
			content.text = objects.Length > 1 ? $"{first.name} [{objects.Length}]" : first.name;
			content.tooltip = string.Join("\n", objects.Where(o => o != null).Select(o => o.name));
			return content;
		}
	}
}