﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Routing
{
    public class BroadcastRoutingLogic : RoutingLogic
    {
        public override Routee Select(object message, Routee[] routees)
        {
            if (routees == null || !routees.Any())
                return Routee.NoRoutee;
            return new SeveralRoutees(routees);
        }
    }

    /// <summary>
    /// Class BroadcastPool.
    /// </summary>
    public class BroadcastPool : Pool
    {
        protected BroadcastPool()
        {           
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BroadcastPool"/> class.
        /// </summary>
        /// <param name="config">The configuration.</param>
        public BroadcastPool(Config config) : base(config)
        {
            
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="BroadcastPool"/> class.
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        /// <param name="resizer">The resizer.</param>
        /// <param name="supervisorStrategy">The supervisor strategy.</param>
        /// <param name="routerDispatcher">The router dispatcher.</param>
        /// <param name="usePoolDispatcher">if set to <c>true</c> [use pool dispatcher].</param>
        public BroadcastPool(int nrOfInstances, Resizer resizer,SupervisorStrategy supervisorStrategy, string routerDispatcher, bool usePoolDispatcher = false)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
            
        }

        /// <summary>
        /// Simple form of BroadcastPool constructor
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        public BroadcastPool(int nrOfInstances) : base(nrOfInstances, null, Pool.DefaultStrategy, null) { }

        /// <summary>
        /// Creates the router.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <returns>Router.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new BroadcastRoutingLogic());
        }

        public override Pool WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            return new BroadcastPool(NrOfInstances, Resizer, strategy, RouterDispatcher, UsePoolDispatcher);
        }

        public override Pool WithResizer(Resizer resizer)
        {
            return new BroadcastPool(NrOfInstances, resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
        }
    }

    public class BroadcastGroup : Group
    {        
        protected BroadcastGroup()
        {
            
        }
        /// <summary>
        ///     Initializes a new instance of the <see cref="BroadcastGroup" /> class.
        /// </summary>
        /// <param name="config">The configuration.</param>
        public BroadcastGroup(Config config)
            : base(config.GetStringList("routees.paths"))
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="BroadcastGroup" /> class.
        /// </summary>
        /// <param name="paths">The paths.</param>
        public BroadcastGroup(params string[] paths)
            : base(paths)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="BroadcastGroup" /> class.
        /// </summary>
        /// <param name="paths">The paths.</param>
        public BroadcastGroup(IEnumerable<string> paths) : base(paths)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="BroadcastGroup" /> class.
        /// </summary>
        /// <param name="routees">The routees.</param>
        public BroadcastGroup(IEnumerable<ActorRef> routees)
            : base(routees)
        {
        }

        /// <summary>
        ///     Creates the router.
        /// </summary>
        /// <returns>Router.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new BroadcastRoutingLogic());
        }
    }
}
