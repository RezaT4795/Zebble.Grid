﻿namespace Zebble
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    public interface IGridCell<TSource> { TSource Item { get; set; } }

    public class GridCell<TSource> : Stack, IGridCell<TSource>
    {
        public TSource Item { get; set; }
    }

    public partial class Grid<TSource, TCellTemplate> : Grid where TCellTemplate : View, IGridCell<TSource>, new()
    {
        ConcurrentList<TSource> dataSource;
        object DataSourceSyncLock = new object();

        public Grid() : base() { Shown.Handle(OnShown); EmptyTemplateChanged.Handle(OnEmptyTemplateChanged); }

        protected override string GetStringSpecifier() => typeof(TSource).Name;

        TCellTemplate CreateItem(TSource item) => new TCellTemplate { Item = item }.CssClass("grid-item");

        public Task<TCellTemplate> AddItem(TSource item) => Add(CreateItem(item));

        public bool ExactColumns { get; set; }

        public override async Task OnInitializing()
        {
            await base.OnInitializing();

            await Add(EmptyTemplateView.Ignored(dataSource.Any()));

            if (ItemViews.None()) await UpdateSource(DataSource);
        }

        public IEnumerable<TCellTemplate> ItemViews => AllChildren.SelectMany(x => x.AllChildren<TCellTemplate>());

        public List<TSource> DataSource
        {
            get => dataSource.ToList();
            set => UpdateSource(value).RunInParallel();
        }

        async Task OnEmptyTemplateChanged(GridEmptyTemplateChangedArg args)
        {
            if (!AllChildren.Contains(args.OldView)) return;

            await Remove(args.OldView);
            await Add(args.NewView.Ignored(dataSource.Any()));
        }

        public async Task UpdateSource(IEnumerable<TSource> source)
        {
            lock (DataSourceSyncLock) dataSource = new ConcurrentList<TSource>(source);

            foreach (var item in AllChildren.Except(EmptyTemplateView).Reverse().ToArray())
                await Remove(item);

            EmptyTemplateView.Style.Ignored = dataSource.Any();

            if (LazyLoad)
            {
                if (IsShown) await LazyLoadInitialItems();
            }
            else
            {
                var views = DataSource.Select(x => new TCellTemplate { Item = x }).ToArray();

                foreach (var v in views) await Add(v);

                if (ExactColumns) await EnsureFullColumns();
            }
        }

        public override void Dispose()
        {
            ParentScroller?.UserScrolledVertically.RemoveHandler(OnUserScrolledVertically);
            base.Dispose();
        }
    }

    public class GridEmptyTemplateChangedArg
    {
        public GridEmptyTemplateChangedArg(View oldView, View newView)
        {
            OldView = oldView;
            NewView = newView;
        }

        public View OldView { get; set; }
        public View NewView { get; set; }
    };

    public class Grid : Stack
    {
        public View EmptyTemplateView = new TextView { Id = "EmptyTextLabel" };
        public readonly AsyncEvent<GridEmptyTemplateChangedArg> EmptyTemplateChanged = new AsyncEvent<GridEmptyTemplateChangedArg>();

        public string EmptyText
        {
            get
            {
                var emptyTextView = EmptyTemplateView as TextView;
                if (emptyTextView == null) return string.Empty;
                else return emptyTextView.Text;
            }
            set
            {
                var emptyTextView = EmptyTemplateView as TextView;
                if (emptyTextView == null) return;
                else emptyTextView.Text = value;
            }
        }

        public View EmptyTemplate
        {
            get => EmptyTemplateView;
            set
            {
                EmptyTemplateChanged.Raise(new GridEmptyTemplateChangedArg(EmptyTemplateView, value));
                EmptyTemplateView = value;
            }
        }
        Stack CurrentStack;
        public int Columns { get; set; } = 2;

        public override async Task<TView> AddAt<TView>(int index, TView child, bool awaitNative = false)
        {
            if (child == EmptyTemplateView)
            {
                await base.AddAt(index, child, awaitNative);
                return child;
            }

            if (index < AllChildren.Count)
                throw new NotImplementedException("AddAt() for adding items in the middle of a grid is not implemented yet.");

            if (CurrentStack == null || CurrentStack.IsDisposing || CurrentStack.AllChildren.Count == Columns)
            {
                // Create a new row:
                await base.AddAt(AllChildren.Count,
                    CurrentStack = new Stack { Direction = RepeatDirection.Horizontal, Id = "Grid-Row" }, awaitNative)
                    .DropContext();
            }

            return await CurrentStack.Add(child, awaitNative).DropContext();
        }

        /// <summary>
        /// If the number of items in the last row is less than the columns, it adds empty canvas cells to fill it up.
        /// This is useful for when you want to benefit from the automatic width allocation.
        /// </summary>
        public async Task EnsureFullColumns()
        {
            if (CurrentStack == null) return;

            while (CurrentStack.AllChildren.Count < Columns)
                await CurrentStack.Add(new Canvas());
        }
    }
}