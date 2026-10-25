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

		// Drag to reorder. Rects are from the last non-layout pass, in scroll view coordinates.
		private readonly List<Rect> m_EntryRects = new List<Rect>();
		private int m_PressedIndex = -1;
		private Vector2 m_PressedPosition;
		private bool m_Dragging;
		private int m_DropIndex = -1; // insertion slot, 0..HistoryCount

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

			// Handled before the buttons, so they never see the drag's MouseDrag / MouseUp.
			int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
			HandleDrag(dragControlId, vertical, ref moveFrom, ref moveTo);

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

				if (m_Dragging && i == m_PressedIndex) {
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

			if (m_Dragging && evt.type == EventType.Repaint) {
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

		private void HandleDrag(int controlId, bool vertical, ref int moveFrom, ref int moveTo)
		{
			var evt = Event.current;

			if (m_Dragging && GUIUtility.hotControl != controlId) {
				// Lost the mouse capture somehow (focus change, etc.).
				StopDragging();
			}

			switch (evt.type) {
				case EventType.MouseDrag:
					if (!m_Dragging && m_PressedIndex >= 0 && m_PressedIndex < m_EntryRects.Count
						&& Vector2.Distance(evt.mousePosition, m_PressedPosition) > 6f) {
						// Steal the capture from the pressed button, so releasing the mouse won't count as a click.
						m_Dragging = true;
						GUIUtility.hotControl = controlId;
					}

					if (m_Dragging) {
						m_DropIndex = GetDropIndex(evt.mousePosition, vertical);
						evt.Use();
						Repaint();
					}
					break;

				case EventType.MouseUp:
					if (m_Dragging) {
						// Removing the entry first shifts every later slot down by one.
						int to = m_DropIndex > m_PressedIndex ? m_DropIndex - 1 : m_DropIndex;
						if (to != m_PressedIndex) {
							moveFrom = m_PressedIndex;
							moveTo = to;
						}
						StopDragging();
						evt.Use();
					}
					break;

				case EventType.KeyDown:
					if (m_Dragging && evt.keyCode == KeyCode.Escape) {
						StopDragging();
						evt.Use();
					}
					break;
			}
		}

		private void StopDragging()
		{
			if (m_Dragging) {
				GUIUtility.hotControl = 0;
			}
			m_Dragging = false;
			m_PressedIndex = -1;
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