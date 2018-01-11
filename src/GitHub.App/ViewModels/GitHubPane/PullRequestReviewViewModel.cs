﻿using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using GitHub.App;
using GitHub.Extensions;
using GitHub.Factories;
using GitHub.Logging;
using GitHub.Models;
using GitHub.Services;
using ReactiveUI;
using Serilog;
using static System.FormattableString;

namespace GitHub.ViewModels.GitHubPane
{
    /// <summary>
    /// View model for displaying details of a pull request review.
    /// </summary>
    [Export(typeof(IPullRequestReviewViewModel))]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class PullRequestReviewViewModel : PanePageViewModelBase, IPullRequestReviewViewModel
    {
        static readonly ILogger log = LogManager.ForContext<PullRequestReviewViewModel>();

        readonly IPullRequestService pullRequestsService;
        readonly IPullRequestSessionManager sessionManager;
        readonly IModelServiceFactory modelServiceFactory;
        IModelService modelService;
        IPullRequestSession session;
        IPullRequestReviewModel model;
        string state;
        bool isPending;
        string body;

        /// <summary>
        /// Initializes a new instance of the <see cref="PullRequestReviewViewModel"/> class.
        /// </summary>
        /// <param name="pullRequestsService">The pull requests service.</param>
        /// <param name="sessionManager">The session manager.</param>
        /// <param name="modelServiceFactory">The model service factory.</param>
        /// <param name="files">The pull request files view model.</param>
        [ImportingConstructor]
        public PullRequestReviewViewModel(
            IPullRequestService pullRequestsService,
            IPullRequestSessionManager sessionManager,
            IModelServiceFactory modelServiceFactory,
            IPullRequestFilesViewModel files)
        {
            Guard.ArgumentNotNull(pullRequestsService, nameof(pullRequestsService));
            Guard.ArgumentNotNull(sessionManager, nameof(sessionManager));
            Guard.ArgumentNotNull(modelServiceFactory, nameof(modelServiceFactory));
            Guard.ArgumentNotNull(files, nameof(files));

            this.pullRequestsService = pullRequestsService;
            this.sessionManager = sessionManager;
            this.modelServiceFactory = modelServiceFactory;

            Files = files;
            NavigateToPullRequest = ReactiveCommand.Create().OnExecuteCompleted(_ => 
                NavigateTo(Invariant($"{LocalRepository.Owner}/{LocalRepository.Name}/pull/{PullRequestNumber}")));
            Submit = ReactiveCommand.CreateAsyncTask(DoSubmit);
        }

        /// <inheritdoc/>
        public ILocalRepositoryModel LocalRepository { get; private set; }

        /// <inheritdoc/>
        public string RemoteRepositoryOwner { get; private set; }

        /// <inheritdoc/>
        public int PullRequestNumber { get; private set; }

        /// <inheritdoc/>
        public long PullRequestReviewId { get; private set; }

        /// <inheritdoc/>
        public IPullRequestFilesViewModel Files { get; }

        /// <inheritdoc/>
        public IPullRequestReviewModel Model
        {
            get { return model; }
            private set { this.RaiseAndSetIfChanged(ref model, value); }
        }

        /// <inheritdoc/>
        public string State
        {
            get { return state; }
            private set { this.RaiseAndSetIfChanged(ref state, value); }
        }

        /// <inheritdoc/>
        public bool IsPending
        {
            get { return isPending; }
            private set { this.RaiseAndSetIfChanged(ref isPending, value); }
        }

        /// <inheritdoc/>
        public string Body
        {
            get { return body; }
            private set { this.RaiseAndSetIfChanged(ref body, value); }
        }

        /// <inheritdoc/>
        public ReactiveCommand<object> NavigateToPullRequest { get; }

        /// <inheritdoc/>
        public ReactiveCommand<Unit> Submit { get; }

        /// <inheritdoc/>
        public async Task InitializeAsync(
            ILocalRepositoryModel localRepository,
            IConnection connection,
            string owner,
            string repo,
            int pullRequestNumber,
            long pullRequestReviewId)
        {
            if (repo != localRepository.Name)
            {
                throw new NotSupportedException();
            }

            IsLoading = true;

            try
            {
                LocalRepository = localRepository;
                RemoteRepositoryOwner = owner;
                PullRequestNumber = pullRequestNumber;
                PullRequestReviewId = pullRequestReviewId;
                modelService = await modelServiceFactory.CreateAsync(connection);
                await Refresh();
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <inheritdoc/>
        public Task InitializeNewAsync(
            ILocalRepositoryModel localRepository,
            IConnection connection,
            string owner,
            string repo,
            int pullRequestNumber)
        {
            return InitializeAsync(localRepository, connection, owner, repo, pullRequestNumber, 0);
        }

        /// <inheritdoc/>
        public override async Task Refresh()
        {
            try
            {
                Error = null;
                IsBusy = true;
                var pullRequest = await modelService.GetPullRequest(RemoteRepositoryOwner, LocalRepository.Name, PullRequestNumber);
                await Load(pullRequest);
            }
            catch (Exception ex)
            {
                log.Error(
                    ex,
                    "Error loading pull request review {Owner}/{Repo}/{Number}/{PullRequestReviewId} from {Address}",
                    RemoteRepositoryOwner,
                    LocalRepository.Name,
                    PullRequestNumber,
                    PullRequestReviewId,
                    modelService.ApiClient.HostAddress.Title);
                Error = ex;
                IsBusy = false;
            }
        }

        /// <inheritdoc/>
        public async Task Load(IPullRequestModel pullRequest)
        {
            try
            {
                session = await sessionManager.GetSession(pullRequest);

                if (PullRequestReviewId > 0)
                {
                    Model = pullRequest.Reviews.Single(x => x.Id == PullRequestReviewId);
                    State = PullRequestDetailReviewItem.ToString(Model.State);
                    IsPending = Model.State == Octokit.PullRequestReviewState.Pending;
                    Body = IsPending || !string.IsNullOrWhiteSpace(Model.Body) ? 
                        Model.Body :
                        Resources.NoDescriptionProvidedMarkdown;
                }
                else
                {
                    Model = null;
                    State = null;
                    IsPending = true;
                    Body = string.Empty;
                    session.StartReview();
                }

                var changes = await pullRequestsService.GetTreeChanges(LocalRepository, pullRequest);
                await Files.InitializeAsync(session, changes, FilterComments);
            }
            finally
            {
                IsBusy = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                Files.Dispose();
            }
        }

        async Task DoSubmit(object arg)
        {
            try
            {
                Octokit.PullRequestReviewEvent e;

                if (Enum.TryParse(arg.ToString(), out e))
                {
                    await session.PostPendingReview(Body, e);
                    Close();
                }
            }
            catch (Exception ex)
            {
                Error = ex;
            }
        }

        bool FilterComments(IInlineCommentThreadModel thread)
        {
            return thread.Comments.Any(x => x.PullRequestReviewId == PullRequestReviewId);
        }
    }
}