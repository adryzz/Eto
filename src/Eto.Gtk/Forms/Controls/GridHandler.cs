using System;
using Eto.Forms;
using System.Linq;
using System.Collections.Generic;
using Eto.GtkSharp.Forms.Cells;
using Eto.GtkSharp.Forms.Menu;
using Eto.Drawing;

namespace Eto.GtkSharp.Forms.Controls
{
	class GridHandler
	{
		internal static readonly object Border_Key = new object();
		internal static readonly object RowDataColumn_Key = new object();
		internal static readonly object ItemDataColumn_Key = new object();
		internal static readonly object AllowColumnReordering_Key = new object();
		internal static readonly object AllowEmptySelection_Key = new object();
	}

	public abstract class GridHandler<TWidget, TCallback> : GtkControl<Gtk.ScrolledWindow, TWidget, TCallback>, Grid.IHandler, ICellDataSource, IGridHandler
		where TWidget : Grid
		where TCallback : Grid.ICallback
	{
		ColumnCollection columns;
		Gtk.TreeViewColumn spacingColumn;
		ContextMenu contextMenu;
		readonly Dictionary<int, int> columnMap = new Dictionary<int, int>();

		protected bool SkipSelectedChange { get; set; }

		protected Gtk.TreeView Tree { get; private set; }

		Gtk.TreeView IGridHandler.Tree => Tree;

		protected Dictionary<int, int> ColumnMap { get { return columnMap; } }

		public override Gtk.Widget EventControl => Tree;

		public override Gtk.Widget DragControl => Tree;

		class EtoScrolledWindow : Gtk.ScrolledWindow
		{
			WeakReference handler;
			public IGtkControl Handler { get => handler?.Target as IGtkControl; set => handler = new WeakReference(value); }

#if GTKCORE			

			protected override void OnGetPreferredWidth(out int minimum_width, out int natural_width)
			{
				base.OnGetPreferredWidth(out minimum_width, out natural_width);

				var h = Handler;
				if (h != null)
				{
					var size = h.UserPreferredSize;
					if (size.Width >= 0)
					{
						natural_width = Math.Min(size.Width, natural_width);
						minimum_width = Math.Min(size.Width, minimum_width);
					}
				}
			}

			protected override void OnGetPreferredHeight(out int minimum_height, out int natural_height)
			{
				base.OnGetPreferredHeight(out minimum_height, out natural_height);
				var h = Handler;
				if (h != null)
				{
					var size = h.UserPreferredSize;
					if (size.Height >= 0)
					{
						natural_height = Math.Min(size.Height, natural_height);
						minimum_height = Math.Min(size.Height, minimum_height);
					}
				}
			}
#endif
		}

		protected GridHandler()
		{
			Control = new EtoScrolledWindow
			{
				Handler = this,
				ShadowType = Gtk.ShadowType.In,
#if GTKCORE
				PropagateNaturalHeight = true,
				PropagateNaturalWidth = true
#endif
			};
		}

		protected abstract ITreeModelImplementor CreateModelImplementor();

		protected void UpdateModel()
		{
			SkipSelectedChange = true;
			var selected = SelectedRows;
			Tree.Model = new Gtk.TreeModelAdapter(CreateModelImplementor());
			SetSelectedRows(selected);
			SkipSelectedChange = false;
		}
		
		protected (double? hscroll, double? vscroll) SaveScrollState()
		{
			var hscrollbar = Control.HScrollbar as Gtk.Scrollbar;
			var vscrollbar = Control.VScrollbar as Gtk.Scrollbar;
			var hscroll = hscrollbar?.Value;
			var vscroll = vscrollbar?.Value;
			return (hscroll, vscroll);
		}
		
		protected void RestoreScrollState((double? hscroll, double? vscroll) state)
		
		{
			var hscrollbar = Control.HScrollbar as Gtk.Scrollbar;
			var vscrollbar = Control.VScrollbar as Gtk.Scrollbar;
			if (state.hscroll != null)
				hscrollbar.Value = state.hscroll.Value;
			if (state.vscroll != null)
				vscrollbar.Value = state.vscroll.Value;
		}

		protected override void Initialize()
		{
			Tree = new Gtk.TreeView();
			// always need this one so the last column doesn't expand
			Tree.AppendColumn(spacingColumn = new Gtk.TreeViewColumn());
			Tree.ColumnDragFunction = Connector.HandleColumnDrag;

			UpdateModel();
			Tree.HeadersVisible = true;
			Control.Add(Tree);

			Tree.Events |= Gdk.EventMask.ButtonPressMask;
			Tree.ButtonPressEvent += Connector.HandleButtonPress;

			columns = new ColumnCollection { Handler = this };
			columns.Register(Widget.Columns);
			base.Initialize();

			Widget.MouseDown += Widget_MouseDown;

		}

		protected new GridConnector Connector => (GridConnector)base.Connector;

		protected override WeakConnector CreateConnector()
		{
			return new GridConnector();
		}

		protected class GridConnector : GtkControlConnector
		{
			public new GridHandler<TWidget, TCallback> Handler { get { return (GridHandler<TWidget, TCallback>)base.Handler; } }

			[GLib.ConnectBefore]
			public void HandleButtonPress(object o, Gtk.ButtonPressEventArgs args)
			{
				var treeview = o as Gtk.TreeView;

				// Gtk default behaviout for multiselect treeview is that
				// left and right click act the same, which is problematic
				// when it comes to user selecting multiple items and than
				// right clicking to find only one item remains selected
				// or if ctrl is held the current item gets unselected.
				if (args.Event.Button == 3 && treeview != null)
				{
					Gtk.TreeViewDropPosition pos;
					Gtk.TreePath path;

					if (treeview.GetDestRowAtPos((int)args.Event.X, (int)args.Event.Y, out path, out pos))
					{
						var height = 0;
						if (treeview.HeadersVisible && treeview.Columns.Length > 0)
							height = treeview.GetCellArea(path, treeview.Columns[0]).Height;

						if (treeview.GetDestRowAtPos((int)args.Event.X, (int)args.Event.Y + height, out path, out pos))
						{
							var paths = treeview.Selection.GetSelectedRows().ToList();

							if (paths.Contains(path))
								args.RetVal = true;
						}
					}
				}

				var handler = Handler;
				if (handler == null)
					return;
					
				if (handler.contextMenu != null && args.Event.Button == 3 && args.Event.Type == Gdk.EventType.ButtonPress)
				{
					var menu = ((ContextMenuHandler)handler.contextMenu.Handler).Control;
					menu.Popup();
					menu.ShowAll();
				}
			}

			int[] selectedRows;

			static bool ArraysEqual<T>(T[] a1, T[] a2)
			{
				if (ReferenceEquals(a1, a2))
					return true;

				if (a1 == null || a2 == null)
					return false;

				if (a1.Length != a2.Length)
					return false;

				EqualityComparer<T> comparer = EqualityComparer<T>.Default;
				for (int i = 0; i < a1.Length; i++)
				{
					if (!comparer.Equals(a1[i], a2[i]))
						return false;
				}
				return true;
			}

			public void HandleGridSelectionChanged(object sender, EventArgs e)
			{
				var handler = Handler;
				if (handler == null)
					return;
				if (!handler.SkipSelectedChange)
				{
					var selected = handler.SelectedRows.ToArray();
					if (!ArraysEqual(selectedRows, selected))
					{
						handler.Callback.OnSelectionChanged(handler.Widget, EventArgs.Empty);
						selectedRows = selected;
					}
				}
			}

			[GLib.ConnectBefore]
			public virtual void OnTreeButtonPress(object sender, Gtk.ButtonPressEventArgs e)
			{
				if (e.Event.Type == Gdk.EventType.TwoButtonPress || e.Event.Type == Gdk.EventType.ThreeButtonPress)
					return;
				var h = Handler;
				if (h == null)
					return;

				Gtk.TreePath path;
				Gtk.TreeViewColumn clickedColumn;

				// Get path and column from mouse position
				h.Tree.GetPathAtPos((int)e.Event.X, (int)e.Event.Y, out path, out clickedColumn);
				if (path == null || clickedColumn == null)
					return;

				var rowIndex = h.GetRowIndexOfPath(path);
				var columnIndex = h.GetColumnOfItem(clickedColumn);
				var item = h.GetItem(path);
				var column = columnIndex == -1 || columnIndex >= h.Widget.Columns.Count ? null : h.Widget.Columns[columnIndex];

				var loc = h.PointFromScreen(new PointF((float)e.Event.XRoot, (float)e.Event.YRoot));

				if (ReferenceEquals(h.Tree.ExpanderColumn, clickedColumn))
				{
					var cellArea = h.Tree.GetCellArea(path, clickedColumn);
					if (loc.X < cellArea.Left && loc.X >= cellArea.Left - 18) // how do we get the size of the expander?
					{
						// clicked on the expander, don't fire the CellClick event
						return;
					}
				}

				h.Callback.OnCellClick(h.Widget, new GridCellMouseEventArgs(column, rowIndex, columnIndex, item, e.Event.ToEtoMouseButtons(), e.Event.State.ToEtoKey(), loc));
			}

			[GLib.ConnectBefore]
			public virtual void HandleGridColumnsChanged(object sender, EventArgs e)
			{
				var h = Handler;
				if (h == null)
					return;
				h.Callback.OnColumnOrderChanged(h.Widget, new GridColumnEventArgs(h.Widget.Columns.First()));
			}

			public virtual bool HandleColumnDrag(Gtk.TreeView tree_view, Gtk.TreeViewColumn column, Gtk.TreeViewColumn prev_column, Gtk.TreeViewColumn next_column)
			{
				var h = Handler;
				if (h == null)
					return true;
				return prev_column != h.spacingColumn;
			}
		}

		public override void AttachEvent(string id)
		{
			switch (id)
			{
				case Grid.ColumnHeaderClickEvent:
				case Grid.CellEditingEvent:
				case Grid.CellEditedEvent:
				case Grid.CellFormattingEvent:
					SetupColumnEvents();
					break;
				case Grid.CellClickEvent:
					Tree.ButtonPressEvent += Connector.OnTreeButtonPress;
					break;
				case Grid.CellDoubleClickEvent:
					Tree.RowActivated += (sender, e) =>
					{
						var rowIndex = GetRowIndexOfPath(e.Path);
						var columnIndex = GetColumnOfItem(e.Column);
						var item = GetItem(e.Path);
						var column = columnIndex == -1 ? null : Widget.Columns[columnIndex];
						Callback.OnCellDoubleClick(Widget, new GridCellMouseEventArgs(column, rowIndex, columnIndex, item, Mouse.Buttons, Keyboard.Modifiers, PointFromScreen(Mouse.Position)));
					};
					break;
				case Grid.SelectionChangedEvent:
					Tree.Selection.Changed += Connector.HandleGridSelectionChanged;
					break;
				case Grid.ColumnOrderChangedEvent:
					Tree.ColumnsChanged += Connector.HandleGridColumnsChanged;
					break;
				default:
					base.AttachEvent(id);
					break;
			}
		}


		public override void OnLoadComplete(EventArgs e)
		{
			base.OnLoadComplete(e);
			UpdateColumns();
		}

		void SetupColumnEvents()
		{
			if (!Widget.Loaded)
				return;
			foreach (var col in Widget.Columns.Select(r => r.Handler).OfType<GridColumnHandler>())
			{
				col.SetupEvents();
			}
		}

		public int RowDataColumn => 0;
		public int ItemDataColumn => 1;

		protected virtual void UpdateColumns()
		{
			if (!Widget.Loaded)
				return;
			columnMap.Clear();
			int columnIndex = 0;
			int dataIndex = 2; // skip RowDataColumn and ItemDataColumn
			
			for (int i = 0; i < Widget.Columns.Count; i++)
			{
				var col = Widget.Columns[i];
				var colHandler = (GridColumnHandler)col.Handler;
				colHandler.Control.Reorderable = AllowColumnReordering;
				colHandler.SetupCell(this, this, columnIndex++, ref dataIndex);
				if (i == 0)
					Tree.ExpanderColumn = colHandler.Control;
			}
			
			foreach (var col in Widget.Columns.OrderBy(r => r.DisplayIndex))
			{
				((GridColumnHandler)col.Handler).SetDisplayIndex();
			}
			
		}

		class ColumnCollection : EnumerableChangedHandler<GridColumn, GridColumnCollection>
		{
			public GridHandler<TWidget, TCallback> Handler { get; set; }

			public override void AddItem(GridColumn item)
			{
				var colhandler = (GridColumnHandler)item.Handler;
#if GTKCORE
				Handler.Tree.InsertColumn(colhandler.Control, (int)Handler.Tree.NColumns - 1);
#else
				Handler.Tree.InsertColumn(colhandler.Control, Count);
#endif
				Handler.UpdateColumns();
			}

			public override void InsertItem(int index, GridColumn item)
			{
				var colhandler = (GridColumnHandler)item.Handler;
				Handler.Tree.InsertColumn(colhandler.Control, index);
				Handler.UpdateColumns();
			}

			public override void RemoveItem(int index)
			{
				var colhandler = (GridColumnHandler)Handler.Widget.Columns[index].Handler;
				Handler.Tree.RemoveColumn(colhandler.Control);
				Handler.UpdateColumns();
			}

			public override void RemoveAllItems()
			{
				foreach (var col in Handler.Tree.Columns)
				{
					Handler.Tree.RemoveColumn(col);
				}
				Handler.Tree.AppendColumn(Handler.spacingColumn);
				Handler.UpdateColumns();
			}

		}

		public bool ShowHeader
		{
			get { return Tree.HeadersVisible; }
			set { Tree.HeadersVisible = value; }
		}


		public bool AllowColumnReordering
		{
			get { return Widget.Properties.Get<bool>(GridHandler.AllowColumnReordering_Key, false); }
			set
			{
				Widget.Properties.Set(GridHandler.AllowColumnReordering_Key, value, false);
				UpdateColumns();
			}
		}

		public int NumberOfColumns
		{
			get
			{
				return Widget.Columns.Count;
			}
		}

		public abstract object GetItem(Gtk.TreePath path);

		public int GetColumnOfItem(Gtk.TreeViewColumn item)
		{
			return Widget.Columns.Select(r => r.Handler as GridColumnHandler).Select(r => r.Control).ToList().IndexOf(item);
		}

		public virtual int GetRowIndexOfPath(Gtk.TreePath path)
		{
			int rowIndex = 0;
			if (path.Indices.Length > 0)
			{
				for (int i = 0; i < path.Depth; i++)
					rowIndex += path.Indices[i] + 1;

				rowIndex--;
			}
			else
				rowIndex = -1;

			return rowIndex;
		}

		public abstract Gtk.TreeIter GetIterAtRow(int row);

		public abstract Gtk.TreePath GetPathAtRow(int row);

		public void SetColumnMap(int dataIndex, int column)
		{
			columnMap[dataIndex] = column;
		}

		public ContextMenu ContextMenu
		{
			get { return contextMenu; }
			set { contextMenu = value; }
		}

		public void EndCellEditing(Gtk.TreePath path, int column)
		{
			var row = path.Indices.Length > 0 ? path.Indices[0] : -1;
			var item = GetItem(path);
			Callback.OnCellEdited(Widget, new GridViewCellEventArgs(Widget.Columns[column], row, column, item));
		}

		public void BeginCellEditing(Gtk.TreePath path, int column)
		{
			var row = path.Indices.Length > 0 ? path.Indices[0] : -1;
			var item = GetItem(path);
			Callback.OnCellEditing(Widget, new GridViewCellEventArgs(Widget.Columns[column], row, column, item));
		}

		public void ColumnClicked(GridColumnHandler column)
		{
			Callback.OnColumnHeaderClick(Widget, new GridColumnEventArgs(column.Widget));
		}

		public bool AllowMultipleSelection
		{
			get { return Tree.Selection.Mode == Gtk.SelectionMode.Multiple; }
			set { Tree.Selection.Mode = value ? Gtk.SelectionMode.Multiple : Gtk.SelectionMode.Browse; }
		}

		public virtual IEnumerable<int> SelectedRows
		{
			get
			{
				return Tree.Selection.GetSelectedRows().Select(r => r.Indices[0]);
			}
			set
			{
				SkipSelectedChange = true;
				SetSelectedRows(value);
				SkipSelectedChange = false;
				Callback.OnSelectionChanged(Widget, EventArgs.Empty);
			}
		}

		protected abstract void SetSelectedRows(IEnumerable<int> value);

		public int RowHeight
		{
			get;
			set;
		}

		public void SelectAll()
		{
			Tree.Selection.SelectAll();
		}

		public void SelectRow(int row)
		{
			Tree.Selection.SelectIter(GetIterAtRow(row));
		}

		public void UnselectRow(int row)
		{
			Tree.Selection.UnselectIter(GetIterAtRow(row));
		}

		public void UnselectAll()
		{
			Tree.Selection.UnselectAll();
		}

		public void BeginEdit(int row, int column)
		{
			var nameColumn = Tree.Columns[column];
			var cellRenderer = nameColumn.Cells[0];
			var path = Tree.Model.GetPath(GetIterAtRow(row));
			Tree.Model.IterNChildren();
			Tree.SetCursorOnCell(path, nameColumn, cellRenderer, true);
		}

		public bool CommitEdit()
		{
			Gtk.TreePath path;
			Gtk.TreeViewColumn column;
			Tree.GetCursor(out path, out column);
			if (path == null || column == null)
				return true;

			// This is a hack, but it works to commit editing.  Is there a better way?
			if (Tree.FocusChild?.HasFocus == true)
				Tree.ChildFocus(Gtk.DirectionType.TabForward);
			return true;
		}

		public bool CancelEdit()
		{
			Gtk.TreePath path;
			Gtk.TreeViewColumn column;
			Tree.GetCursor(out path, out column);
			if (path == null || column == null)
				return true;

			// This is a hack, but it works to abort editing.  Is there a better way?
			if (Tree.FocusChild?.HasFocus == true)
				Tree.GrabFocus();
			return true;
		}

		public void OnCellFormatting(GridCellFormatEventArgs args)
		{
			Callback.OnCellFormatting(Widget, args);
		}

		public void ScrollToRow(int row)
		{
			var path = this.GetPathAtRow(row);
			var column = Tree.Columns.First();
			Tree.ScrollToCell(path, column, false, 0, 0);
		}

		public GridLines GridLines
		{
			get
			{
				switch (Tree.EnableGridLines)
				{
					case Gtk.TreeViewGridLines.None:
						return GridLines.None;
					case Gtk.TreeViewGridLines.Horizontal:
						return GridLines.Horizontal;
					case Gtk.TreeViewGridLines.Vertical:
						return GridLines.Vertical;
					case Gtk.TreeViewGridLines.Both:
						return GridLines.Both;
					default:
						throw new NotSupportedException();
				}
			}
			set
			{
				switch (value)
				{
					case GridLines.None:
						Tree.EnableGridLines = Gtk.TreeViewGridLines.None;
						break;
					case GridLines.Horizontal:
						Tree.EnableGridLines = Gtk.TreeViewGridLines.Horizontal;
						break;
					case GridLines.Vertical:
						Tree.EnableGridLines = Gtk.TreeViewGridLines.Vertical;
						break;
					case GridLines.Both:
						Tree.EnableGridLines = Gtk.TreeViewGridLines.Both;
						break;
					default:
						throw new NotSupportedException();
				}
			}
		}

		public BorderType Border
		{
			get { return Widget.Properties.Get(GridHandler.Border_Key, BorderType.Bezel); }
			set { Widget.Properties.Set(GridHandler.Border_Key, value, () => Control.ShadowType = value.ToGtk(), BorderType.Bezel); }
		}

		public bool IsEditing
		{
			get
			{
				Gtk.TreePath path;
				Gtk.TreeViewColumn focus_column;
				Tree.GetCursor(out path, out focus_column);

#if GTK2
				var cells = focus_column?.CellRenderers;
#elif GTK3
				var cells = focus_column?.Cells;
#endif
				return cells?.OfType<IEtoCellRenderer>().Any(r => r.Editing) ?? false;
			}
		}

		protected abstract bool HasRows { get; }

		public bool AllowEmptySelection
		{
			get => Widget.Properties.Get<bool>(GridHandler.AllowEmptySelection_Key, true);
			set => Widget.Properties.TrySet(GridHandler.AllowEmptySelection_Key, value, true);
		}

		private void Widget_MouseDown(object sender, MouseEventArgs e)
		{
			if (!e.Handled && e.Buttons == MouseButtons.Primary)
			{
				var location = e.Location;
				if (AllowEmptySelection)
				{
					// clicked on an empty area
					if (!Tree.GetPathAtPos((int)location.X, (int)location.Y, out _, out _))
					{
						UnselectAll();
						e.Handled = true;
					}
				}
				else
				{
					// cancel ctrl+clicking to remove last selected item
					if (Tree.GetPathAtPos((int)location.X, (int)location.Y, out var path, out _))
					{
						if (Tree.Model.GetIter(out var iter, path))
						{
							var isSelected = Tree.Selection.IterIsSelected(iter);
							if (e.Modifiers == Keys.Control && isSelected && Tree.Selection.CountSelectedRows() == 1)
							{
								e.Handled = true;
							}
						}
					}
				}
			}
		}

		protected void EnsureSelection()
		{
			if (!AllowEmptySelection && Tree.Selection.CountSelectedRows() == 0)
			{
				SelectRow(0);
			}
		}

		public int GetColumnDisplayIndex(GridColumnHandler column)
		{
			var columns = Tree.Columns;
			return Array.IndexOf(columns, column.Control);
		}

		public void SetColumnDisplayIndex(GridColumnHandler column, int index)
		{
			var columns = Tree.Columns;
			var currentIndex = Array.IndexOf(columns, column.Control);
			if (index != currentIndex)
				Tree.MoveColumnAfter(column.Control, columns[index]);
		}
	}
}

