﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using GitHub.Extensions;
using GitHub.Logging;
using GitHub.Models;
using GitHub.Primitives;
using GitHub.Services;
using ReactiveUI;
using Serilog;

namespace GitHub.ViewModels.Dialog.Clone
{
    [Export(typeof(IRepositorySelectViewModel))]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class RepositorySelectViewModel : ViewModelBase, IRepositorySelectViewModel
    {
        static readonly ILogger log = LogManager.ForContext<RepositorySelectViewModel>();
        readonly IRepositoryCloneService service;
        IConnection connection;
        Exception error;
        string filter;
        bool isEnabled;
        bool isLoading;
        bool loadingStarted;
        IReadOnlyList<IRepositoryItemViewModel> items;
        ICollectionView itemsView;
        ObservableAsPropertyHelper<IRepositoryModel> repository;
        IRepositoryItemViewModel selectedItem;

        [ImportingConstructor]
        public RepositorySelectViewModel(IRepositoryCloneService service)
        {
            Guard.ArgumentNotNull(service, nameof(service));

            this.service = service;

            repository = this.WhenAnyValue(x => x.SelectedItem)
                .Select(CreateRepository)
                .ToProperty(this, x => x.Repository);
            this.WhenAnyValue(x => x.Filter).Subscribe(_ => ItemsView?.Refresh());
        }

        public Exception Error
        {
            get => error;
            private set => this.RaiseAndSetIfChanged(ref error, value);
        }

        public string Filter
        {
            get => filter;
            set => this.RaiseAndSetIfChanged(ref filter, value);
        }

        public bool IsEnabled
        {
            get => isEnabled;
            private set => this.RaiseAndSetIfChanged(ref isEnabled, value);
        }

        public bool IsLoading
        {
            get => isLoading;
            private set => this.RaiseAndSetIfChanged(ref isLoading, value);
        }

        public IReadOnlyList<IRepositoryItemViewModel> Items
        {
            get => items;
            private set => this.RaiseAndSetIfChanged(ref items, value);
        }

        public ICollectionView ItemsView
        {
            get => itemsView;
            private set => this.RaiseAndSetIfChanged(ref itemsView, value);
        }

        public IRepositoryItemViewModel SelectedItem
        {
            get => selectedItem;
            set => this.RaiseAndSetIfChanged(ref selectedItem, value);
        }

        public IRepositoryModel Repository => repository.Value;

        public void Initialize(IConnection connection)
        {
            Guard.ArgumentNotNull(connection, nameof(connection));

            this.connection = connection;
            IsEnabled = true;
        }

        public async Task Activate()
        {
            if (connection == null || loadingStarted) return;

            Error = null;
            IsLoading = true;
            loadingStarted = true;

            try
            {
                var results = await service.ReadViewerRepositories(connection.HostAddress).ConfigureAwait(true);
                Items = results.Select(x => new RepositoryItemViewModel(x)).ToList();
                ItemsView = CollectionViewSource.GetDefaultView(Items);
                ItemsView.Filter = FilterItem;
            }
            catch (Exception ex)
            {
                log.Error(ex, "Error reading repository list from {Address}", connection.HostAddress);
                Error = ex;
            }
            finally
            {
                IsLoading = false;
            }
        }

        bool FilterItem(object obj)
        {
            if (obj is IRepositoryItemViewModel item && !string.IsNullOrWhiteSpace(Filter))
            {
                return item.Caption.Contains(Filter, StringComparison.CurrentCultureIgnoreCase);
            }

            return true;
        }

        IRepositoryModel CreateRepository(IRepositoryItemViewModel item)
        {
            return item != null ? 
                new RepositoryModel(item.Name, UriString.ToUriString(item.Url)) :
                null;
        }
    }
}