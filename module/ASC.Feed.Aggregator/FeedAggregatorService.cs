/* 
 * 
 * (c) Copyright Ascensio System Limited 2010-2014
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as
 * published by the Free Software Foundation, either version 3 of the
 * License, or (at your option) any later version.
 * 
 * http://www.gnu.org/licenses/agpl.html 
 * 
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web;
using ASC.Common.Data;
using ASC.Common.Web;
using ASC.Core;
using ASC.Core.Caching;
using ASC.Feed.Aggregator.Config;
using ASC.Feed.Aggregator.Modules;
using ASC.Feed.Aggregator.Modules.Community;
using ASC.Feed.Aggregator.Modules.CRM;
using ASC.Feed.Aggregator.Modules.Documents;
using ASC.Feed.Aggregator.Modules.Projects;
using ASC.Feed.Data;
using ASC.Projects.Core.Services.NotifyService;
using ASC.Web.Core;
using ASC.Web.Studio.Utility;
using log4net;
using Newtonsoft.Json;

namespace ASC.Feed.Aggregator
{
    public class FeedAggregatorService
    {
        private static readonly ILog log = LogManager.GetLogger("ASC.Feed.Agregator");

        public static readonly List<IFeedModule> Modules = new List<IFeedModule>
            {
                new BlogsModule(),
                new BookmarksModule(),
                //new ForumsModule(),
                new ForumTopicsModule(),
                new ForumPostsModule(),
                new EventsModule(),
                new ContactsModule(),
                new CrmTasksModule(),
                new DealsModule(),
                new CasesModule(),
                new ProjectsModule(),
                new ParticipantsModule(),
                new MilestonesModule(),
                new DiscussionsModule(),
                new TasksModule(),
                new FilesModule(),
                new FoldersModule()
            };

        private Timer aggregateTimer;
        private Timer removeTimer;

        private volatile bool isStopped;
        private readonly object aggregateLock = new object();
        private readonly object removeLock = new object();


        public void Start()
        {
            var cfg = FeedConfigurationSection.GetFeedSection();

            isStopped = false;
            DbRegistry.Configure();
            CommonLinkUtility.Initialize(cfg.ServerRoot);
            WebItemManager.Instance.LoadItems();

            aggregateTimer = new Timer(AggregateFeeds, cfg.AggregateInterval, TimeSpan.Zero, cfg.AggregatePeriod);
            removeTimer = new Timer(RemoveFeeds, cfg.AggregateInterval, cfg.RemovePeriod, cfg.RemovePeriod);
        }

        public void Stop()
        {
            isStopped = true;

            if (aggregateTimer != null)
            {
                aggregateTimer.Change(Timeout.Infinite, Timeout.Infinite);
                aggregateTimer.Dispose();
                aggregateTimer = null;
            }

            if (removeTimer != null)
            {
                removeTimer.Change(Timeout.Infinite, Timeout.Infinite);
                removeTimer.Dispose();
                removeTimer = null;
            }
        }

        private void AggregateFeeds(object interval)
        {
            if (!Monitor.TryEnter(aggregateLock)) return;
            try
            { 
                var start = DateTime.UtcNow;
                log.DebugFormat("Start of collecting feeds...");

                foreach (var module in Modules)
                {
                    try
                    {
                        var result = new List<FeedRow>();
                        var fromTime = FeedAggregateDataProvider.GetLastTimeAggregate(module.GetType().Name);
                        if (fromTime == default(DateTime)) fromTime = DateTime.UtcNow.Subtract((TimeSpan)interval);
                        var toTime = DateTime.UtcNow;

                        var tenants = Attempt(10, () => module.GetTenantsWithFeeds(fromTime)).ToList();
                        log.DebugFormat("Find {1} tenants for module {0}.", module.GetType().Name, tenants.Count());

                        foreach (var tenant in tenants)
                        {
                            // Warning! There is hack here!
                            // clearing the cache to get the correct acl
                            var aspCache = new AspCache();
                            aspCache.Remove("acl" + tenant);
                            aspCache.Remove("/webitemsecurity/" + tenant);
                            aspCache.Remove(string.Format("sub/{0}/{1}/{2}", tenant, "6045b68c-2c2e-42db-9e53-c272e814c4ad", NotifyConstants.Event_NewCommentForMessage.ID));

                            HttpContext.Current = null;
                            try
                            {
                                if (CoreContext.TenantManager.GetTenant(tenant) == null)
                                {
                                    continue;
                                }

                                CoreContext.TenantManager.SetCurrentTenant(tenant);
                                HttpContext.Current = new HttpContext(
                                    new HttpRequest("hack", CommonLinkUtility.GetFullAbsolutePath("/"), string.Empty),
                                    new HttpResponse(new StringWriter()));

                                var feeds = Attempt(10, () => module.GetFeeds(new FeedFilter(fromTime, toTime) { Tenant = tenant }));
                                log.DebugFormat("{0} feeds in {1} tenant.", feeds.Count(), tenant);
                                foreach (var tuple in feeds)
                                {
                                    if (tuple.Item1 == null) continue;

                                    var r = new FeedRow
                                        {
                                            Id = tuple.Item1.Id,
                                            ClearRightsBeforeInsert = tuple.Item1.Variate,
                                            Tenant = tenant,
                                            ProductId = module.Product,
                                            ModuleId = tuple.Item1.Module,
                                            AuthorId = tuple.Item1.LastModifiedBy,
                                            CreatedDate = tuple.Item1.Date,
                                            GroupId = tuple.Item1.GroupId,
                                            Json = JsonConvert.SerializeObject(tuple.Item1, new JsonSerializerSettings
                                                {
                                                    DateTimeZoneHandling = DateTimeZoneHandling.Utc
                                                }),
                                            Keywords = tuple.Item1.Keywords
                                        };

                                    foreach (var u in CoreContext.UserManager.GetUsers())
                                    {
                                        if (isStopped)
                                        {
                                            return;
                                        }
                                        if (TryAuthenticate(u.ID) && module.VisibleFor(tuple.Item1, tuple.Item2, u.ID))
                                        {
                                            r.Users.Add(u.ID);
                                        }
                                    }

                                    result.Add(r);
                                }
                            }
                            catch (Exception ex)
                            {
                                log.ErrorFormat("Tenant: {0}, {1}", tenant, ex);
                            }
                            finally
                            {
                                if (HttpContext.Current != null)
                                {
                                    new DisposableHttpContext(HttpContext.Current).Dispose();
                                    HttpContext.Current = null;
                                }
                            }
                        }

                        FeedAggregateDataProvider.SaveFeeds(result, module.GetType().Name, toTime);

                        log.DebugFormat("Time of collecting news: {0}", DateTime.UtcNow - start);
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex);
                    }
                    finally
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                log.ErrorFormat("Tenant: {0}, {1}", 0, ex);
            }
            Monitor.Exit(aggregateLock);
        }

        private void RemoveFeeds(object interval)
        {
            if (!Monitor.TryEnter(removeLock)) return;

            try
            {
                log.DebugFormat("Start of removing old news");
                FeedAggregateDataProvider.RemoveFeedAggregate(DateTime.UtcNow.Subtract((TimeSpan)interval));
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
            finally
            {
                Monitor.Exit(removeLock);
            }
        }

        private static T Attempt<T>(int count, Func<T> action)
        {
            var counter = 0;
            while (true)
            {
                try
                {
                    return action();
                }
                catch
                {
                    if (count < ++counter)
                    {
                        throw;
                    }
                }
            }
        }

        private static bool TryAuthenticate(Guid userid)
        {
            try
            {
                SecurityContext.AuthenticateMe(CoreContext.Authentication.GetAccountByID(userid));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}