﻿//-----------------------------------------------------------------------
// <copyright file="HeliosTransportDnsResolutionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport.Helios;
using Akka.TestKit;
using Akka.Util.Internal;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.FSharp.Core;
using Config = Akka.Configuration.Config;
// ReSharper disable EmptyGeneralCatchClause

namespace Akka.Remote.Tests.Transport
{

    /// <summary>
    /// Generates a range of random options for DNS
    /// </summary>
    public static class EndpointGenerators
    {
        public static Arbitrary<EndPoint> Endpoints()
        {
            return Arb.From(Underlying());
        }

        private static Gen<EndPoint> Underlying()
        {
            return Gen.Elements<EndPoint>(new IPEndPoint(IPAddress.Loopback, 0),
                new IPEndPoint(IPAddress.IPv6Loopback, 0),
                new DnsEndPoint("localhost", 0), new IPEndPoint(IPAddress.Any.MapToIPv6(), 1337));
        }

        public static Arbitrary<IPEndPoint> IpEndPoints()
        {
            return Arb.From(Gen.Elements<IPEndPoint>(new IPEndPoint(IPAddress.Loopback, 1337),
               new IPEndPoint(IPAddress.IPv6Loopback, 1337),
               new IPEndPoint(IPAddress.Any, 1337), new IPEndPoint(IPAddress.IPv6Any, 1337),
               new IPEndPoint(IPAddress.Any.MapToIPv6(), 1337)));
        }

        /// <summary>
        /// Includes IPV4 / IPV6 "any" addresses
        /// </summary>
        /// <returns></returns>
        public static Arbitrary<EndPoint> AllEndpoints()
        {
            return Arb.From(Gen.Elements<EndPoint>(new IPEndPoint(IPAddress.Loopback, 0),
               new IPEndPoint(IPAddress.IPv6Loopback, 0),
               new DnsEndPoint("localhost", 0), new IPEndPoint(IPAddress.Any, 0), new IPEndPoint(IPAddress.IPv6Any, 0)));
        }

        public static string ParseAddress(EndPoint endpoint)
        {
            if (endpoint is IPEndPoint)
                return ((IPEndPoint)endpoint).Address.ToString();
            return ((DnsEndPoint)endpoint).Host;
        }
    }


    /// <summary>
    /// Designed to guarantee that the default Akka.Remote transport "does the right thing" with respect
    /// to DNS resolution and IP binding under a variety of scenarios
    /// </summary>
    public class HeliosTransportDnsResolutionSpec : AkkaSpec
    {
        public HeliosTransportDnsResolutionSpec()
        {
            Arb.Register(typeof(EndpointGenerators));
        }

        public Config BuildConfig(string hostname, int? port = null, string publichostname = null)
        {
            return ConfigurationFactory.ParseString(@"akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""")
                .WithFallback("akka.remote.helios.tcp.hostname =\"" + hostname + "\"")
                .WithFallback("akka.remote.helios.tcp.public-hostname =\"" + (publichostname ?? hostname) + "\"")
                .WithFallback("akka.remote.helios.tcp.port = " + (port ?? 0))
                .WithFallback("akka.test.single-expect-default = 1s")
                .WithFallback(Sys.Settings.Config);
        }

        private class AssociationAcker : ReceiveActor
        {
            public AssociationAcker()
            {
                ReceiveAny(o => Sender.Tell("ack"));
            }
        }

        private void Setup(string inboundHostname, string outboundHostname, string inboundPublicHostname = null, string outboundPublicHostname = null)
        {
            _inbound = ActorSystem.Create("Sys1", BuildConfig(inboundHostname, 0, inboundPublicHostname));
            _outbound = ActorSystem.Create("Sys2", BuildConfig(outboundHostname, 0, outboundPublicHostname));

            _inbound.ActorOf(Props.Create(() => new AssociationAcker()), "ack");
            _outbound.ActorOf(Props.Create(() => new AssociationAcker()), "ack");

            var addrInbound = RARP.For(_inbound).Provider.DefaultAddress;
            var addrOutbound = RARP.For(_outbound).Provider.DefaultAddress;

            _inboundAck = new RootActorPath(addrInbound) / "user" / "ack";
            _outboundAck = new RootActorPath(addrOutbound) / "user" / "ack";

            _inboundProbe = CreateTestProbe(_inbound);
            _outboundProbe = CreateTestProbe(_outbound);
        }

        private void Cleanup()
        {
            Shutdown(_inbound, TimeSpan.FromSeconds(1));
            Shutdown(_outbound, TimeSpan.FromSeconds(1));
        }

        private ActorSystem _inbound;
        private ActorSystem _outbound;
        private ActorPath _inboundAck;
        private ActorPath _outboundAck;
        private TestProbe _inboundProbe;
        private TestProbe _outboundProbe;

        [Property(MaxTest = 25)]
        public Property HeliosTransport_Should_Resolve_DNS(EndPoint inbound, EndPoint outbound)
        {
            try
            {
                Setup(EndpointGenerators.ParseAddress(inbound), EndpointGenerators.ParseAddress(outbound));
                var outboundReceivedAck = true;
                var inboundReceivedAck = true;
                _outbound.ActorSelection(_inboundAck).Tell("ping", _outboundProbe.Ref);
                try
                {
                    _outboundProbe.ExpectMsg("ack");

                }
                catch
                {
                    outboundReceivedAck = false;
                }

                _inbound.ActorSelection(_outboundAck).Tell("ping", _inboundProbe.Ref);
                try
                {
                    _inboundProbe.ExpectMsg("ack");
                }
                catch
                {
                    inboundReceivedAck = false;
                }


                return outboundReceivedAck.Label($"Expected (outbound: {RARP.For(_outbound).Provider.DefaultAddress}) to be able to successfully message and receive reply from (inbound: {RARP.For(_inbound).Provider.DefaultAddress})")
                    .And(inboundReceivedAck.Label($"Expected (inbound: {RARP.For(_inbound).Provider.DefaultAddress}) to be able to successfully message and receive reply from (outbound: {RARP.For(_outbound).Provider.DefaultAddress})"));
            }
            finally
            {
                Cleanup();
            }
        }

        [Property(MaxTest = 100)]
        public Property HeliosTransport_Should_Resolve_DNS_with_PublicHostname(EndPoint inbound, EndPoint publicInbound,
            EndPoint outbound, EndPoint publicOutbound)
        {
            if (IsAnyIp(publicInbound) || IsAnyIp(publicOutbound)) return true.When(false);
           
            try
            {
                Setup(EndpointGenerators.ParseAddress(inbound),
                    EndpointGenerators.ParseAddress(outbound), 
                    EndpointGenerators.ParseAddress(publicInbound), 
                    EndpointGenerators.ParseAddress(publicOutbound));
                var outboundReceivedAck = true;
                var inboundReceivedAck = true;
                _outbound.ActorSelection(_inboundAck).Tell("ping", _outboundProbe.Ref);
                try
                {
                    _outboundProbe.ExpectMsg("ack");

                }
                catch
                {
                    outboundReceivedAck = false;
                }

                _inbound.ActorSelection(_outboundAck).Tell("ping", _inboundProbe.Ref);
                try
                {
                    _inboundProbe.ExpectMsg("ack");
                }
                catch
                {
                    inboundReceivedAck = false;
                }


                return outboundReceivedAck.Label($"Expected (outbound: {RARP.For(_outbound).Provider.DefaultAddress}) to be able to successfully message and receive reply from (inbound: {RARP.For(_inbound).Provider.DefaultAddress})")
                    .And(inboundReceivedAck.Label($"Expected (inbound: {RARP.For(_inbound).Provider.DefaultAddress}) to be able to successfully message and receive reply from (outbound: {RARP.For(_outbound).Provider.DefaultAddress})"));
            }
            finally
            {
                Cleanup();
            }
        }

        private static bool IsAnyIp(EndPoint publicInbound)
        {
            if (publicInbound is IPEndPoint
                && (publicInbound.AsInstanceOf<IPEndPoint>().Address.Equals(IPAddress.Any)
                || publicInbound.AsInstanceOf<IPEndPoint>().Address.Equals(IPAddress.IPv6Any)))
            {
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Testing for IPV6 issues
        /// </summary>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        [Property]
        public Property HeliosTransport_should_map_valid_IPEndpoints_to_Address(IPEndPoint endpoint)
        {
            var addr = HeliosTransport.MapSocketToAddress(endpoint, "akka.tcp", "foo");
            var parsedEp = (IPEndPoint)HeliosTransport.AddressToSocketAddress(addr);
            return endpoint.Equals(parsedEp).Label("Should be able to parse endpoint to address and back");
        }

        /// <summary>
        /// Testing for IPV6 issues
        /// </summary>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        [Property]
        public Property HeliosTransport_should_map_valid_IPEndpoints_to_ActorPath(IPEndPoint endpoint)
        {
            var addr = HeliosTransport.MapSocketToAddress(endpoint, "akka.tcp", "foo");
            var actorPath = new RootActorPath(addr) / "user" / "foo";
            var serializationFormat = actorPath.ToSerializationFormat();
            var reparsedActorPath = ActorPath.Parse(serializationFormat);
            return actorPath.Equals(reparsedActorPath).Label($"Should be able to parse endpoint to ActorPath and back; expected {actorPath.ToSerializationFormat()} but was {reparsedActorPath.ToSerializationFormat()}");
        }
    }
}

