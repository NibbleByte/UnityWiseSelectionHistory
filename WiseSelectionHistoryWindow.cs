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

		// Dragging an entry starts a regular Unity drag of its objects, so it can be dropped anywhere
		// Unity accepts objects (scene, hierarchy, object fields, ...). Dropping it back on this window reorders.
		// The generic data marks our own drags and carries the dragged entry's index.
		private const string DragDataKey = "WiseSelectionHistoryEntryIndex";

		// Rects are from the last non-layout pass, in scroll view coordinates.
		private readonly List<Rect> m_EntryRects = new List<Rect>();
		private int m_PressedIndex = -1;
		private Vector2 m_PressedPosition;
		private int m_DragFromIndex = -1; // set while one of our drags hovers this window
		private int m_DropIndex = -1;     // insertion slot, 0..HistoryCount

		[MenuItem("Edit/Selection/History Window", priority = 320)]
		static void Open() => GetWindow<WiseSelectionHistoryWindow>("Selection History");

		void OnEnable()
		{
			WiseSelectionHistoryNavigator.HistoryChanged += Repaint;
			WiseSelectionHistoryNavigator.EntryAdded += OnEntryAdded;
			EditorApplication.hierarchyChanged += Repaint; // renamed or deleted objects
		}

		void OnDisable()
		{
			WiseSelectionHistoryNavigator.HistoryChanged -= Repaint;
			WiseSelectionHistoryNavigator.EntryAdded -= OnEntryAdded;
			EditorApplication.hierarchyChanged -= Repaint;
		}

		// Only new selections scroll to the end; Back / Forward and clicks leave the scroll alone.
		void OnEntryAdded() => m_ScrollToEnd = true;

		void OnGUI()
		{
			if (m_EntryStyle == null) {
				m_EntryStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };
			}

			bool vertical = position.height >= position.width;

			// Forget the last pressed entry on any press, before the arrow buttons can consume it.
			// The entry under the mouse (if any) sets it again below.
			if (Event.current.type == EventType.MouseDown) {
				m_PressedIndex = -1;
			}

			var prevIconSize = EditorGUIUtility.GetIconSize();
			EditorGUIUtility.SetIconSize(new Vector2(16, 16));

			// Clicks change the history, which would break the GUILayout pass if done mid-loop,
			// so they are collected here and applied once everything is drawn.
			int clickedIndex = -1;
			int navigateDirection = 0;
			int moveFrom = -1, moveTo = -1;

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

			var evt = Event.current;
			bool recordRects = evt.type != EventType.Layout; // layout pass only returns placeholder rects
			if (recordRects) {
				m_EntryRects.Clear();
			}

			int current = WiseSelectionHistoryNavigator.CurrentIndex;
			var prevBackground = GUI.backgroundColor;
			var prevColor = GUI.color;

			for (int i = 0; i < WiseSelectionHistoryNavigator.HistoryCount; i++) {
				var objects = WiseSelectionHistoryNavigator.GetHistoryEntry(i);
				var first = objects.FirstOrDefault(o => o != null);
				bool isScene = first != null && !EditorUtility.IsPersistent(first);

				if (i == current) {
					GUI.backgroundColor = Color.yellow;
				} else if (isScene) {
					GUI.backgroundColor = new Color(0.65f, 0.85f, 1f);
				}

				if (i == m_DragFromIndex) {
					GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, prevColor.a * 0.4f);
				}

				var options = vertical
					? new[] { GUILayout.ExpandWidth(true) }
					: new[] { GUILayout.ExpandWidth(false), GUILayout.MaxWidth(160f) };

				var content = GetContent(objects, first);
				var rect = GUILayoutUtility.GetRect(content, m_EntryStyle, options);
				if (recordRects) {
					m_EntryRects.Add(rect);
				}

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
				DrawDropMarker(vertical);
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
			} else if (moveFrom >= 0) {
				WiseSelectionHistoryNavigator.MoveEntry(moveFrom, moveTo);
			}
		}

		private void HandleDrag(bool vertical, ref int moveFrom, ref int moveTo)
		{
			var evt = Event.current;

			switch (evt.type) {
				case EventType.MouseDrag:
					if (m_PressedIndex >= 0 && m_PressedIndex < WiseSelectionHistoryNavigator.HistoryCount
						&& Vector2.Distance(evt.mousePosition, m_PressedPosition) > 6f) {
						StartEntryDrag(m_PressedIndex);
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
					m_DropIndex = GetDropIndex(evt.mousePosition, vertical);
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

		private void StartEntryDrag(int index)
		{
			var objects = WiseSelectionHistoryNavigator.GetHistoryEntry(index).Where(o => o != null).ToArray();
			if (objects.Length == 0)
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
			DragAndDrop.SetGenericData(DragDataKey, index);
			DragAndDrop.StartDrag(objects.Length > 1 ? $"{objects[0].name} ({objects.Length})" : objects[0].name);
		}

		private void ClearDropState()
		{
			m_DragFromIndex = -1;
			m_DropIndex = -1;
			Repaint();
		}

		/// <summary>The slot between entries closest to <paramref name="mousePosition"/>, from 0 (before the first) to the entry count (after the last).</summary>
		private int GetDropIndex(Vector2 mousePosition, bool vertical)
		{
			int index = 0;
			foreach (var rect in m_EntryRects) {
				float center = vertical ? rect.center.y : rect.center.x;
				float mouse = vertical ? mousePosition.y : mousePosition.x;
				if (mouse > center) {
					index++;
				}
			}
			return index;
		}

		private void DrawDropMarker(bool vertical)
		{
			if (m_DropIndex < 0 || m_EntryRects.Count == 0)
				return;

			const float Thickness = 2f;
			bool afterLast = m_DropIndex >= m_EntryRects.Count;
			var rect = m_EntryRects[afterLast ? m_EntryRects.Count - 1 : m_DropIndex];

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
			content.text = objects.Length > 1 ? $"{first.name} ({objects.Length})" : first.name;
			content.tooltip = string.Join("\n", objects.Where(o => o != null).Select(o => o.name));
			return content;
		}
	}
}