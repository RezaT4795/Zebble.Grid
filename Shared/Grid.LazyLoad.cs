namespace Zebble
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Zebble.Device;
    using Olive;

    partial class Grid<TSource, TCellTemplate>
    {
        int VisibleItems;
        float GridHeight = 0;
        bool IsLazyLoadingMore, lazyLoad;
        ScrollView ParentScroller;

        /// <summary>
        /// This event will be fired when all datasource items are rendered and added to the grid. 
        /// </summary>
        public readonly AsyncEvent LazyLoadEnded = new AsyncEvent();

        public bool LazyLoad
        {
            get => lazyLoad;
            set { lazyLoad = value; SetPseudoCssState("lazy-loaded", value).RunInParallel(); }
        }

        async Task OnShown()
        {
            if (LazyLoad)
            {
                ParentScroller = FindParent<ScrollView>();
                ParentScroller?.UserScrolledVertically.HandleOn(Thread.Pool, OnUserScrolledVertically);

                await LazyLoadInitialItems();
            }
        }

        async Task OnUserScrolledVertically()
        {
            if (IsLazyLoadingMore) return;
            IsLazyLoadingMore = true;

            var staticallyVisible = ParentScroller.ActualHeight - ActualY;

            var shouldShowUpto = ParentScroller.ScrollY + staticallyVisible + 10 /* Margin to ensure something is there */;

            while (shouldShowUpto >= GridHeight)
            {
                if (!await LazyLoadMore()) break;

                if (OS.Platform.IsIOS()) await Task.Delay(Animation.OneFrame);
            }

            IsLazyLoadingMore = false;
        }

        protected override float CalculateContentAutoHeight()
        {
            if (!LazyLoad) return base.CalculateContentAutoHeight();

            var lastItem = ItemViews.LastOrDefault();
            if (lastItem == null) return 0;

            lastItem.ApplyCssToBranch().Wait();

            if (lastItem.Height.AutoOption.HasValue || lastItem.Height.PercentageValue.HasValue)
                Log.For(this).Error(null, "Items in a lazy loaded grid must have an explicit height value.");

            return Padding.Vertical() +
                (float)Math.Ceiling((double)dataSource.Count / Columns) * lastItem.CalculateTotalHeight();
        }

        Task LazyLoadInitialItems() => UIWorkBatch.Run(DoLazyLoadInitialItems);

        async Task DoLazyLoadInitialItems()
        {
            var visibleHeight = FindParent<ScrollView>()?.ActualHeight ?? Page?.ActualHeight ?? Root.ActualHeight;
            visibleHeight -= ActualY;

            var startIndex = 0;
            VisibleItems = 0;

            GridHeight = ManagedChildren.Sum(i => i.CalculateTotalHeight());

            while (GridHeight < visibleHeight && startIndex < DataSource.Count())
            {
                var item = await AddItem(DataSource[startIndex]);
                startIndex++;
                VisibleItems++;

                if (VisibleItems % Columns == 0)
                    GridHeight += item.ActualHeight;
            }

            if (DataSource.Count == startIndex && ExactColumns)
                await EnsureFullColumns();
        }

        /// <summary>
        /// Returns whether it successfully added one.
        /// </summary>
        async Task<bool> LazyLoadMore()
        {
            TSource next;
            lock (DataSourceSyncLock) next = DataSource.Skip(VisibleItems).FirstOrDefault();

            if (next == null)
            {
                if (ExactColumns) await EnsureFullColumns();
                await LazyLoadEnded.Raise();
                return false;
            }

            VisibleItems++;
            var item = CreateItem(next);
            await Add(item);

            if (VisibleItems % Columns == 0)
                GridHeight += item.ActualHeight;

            return true;
        }
    }
}