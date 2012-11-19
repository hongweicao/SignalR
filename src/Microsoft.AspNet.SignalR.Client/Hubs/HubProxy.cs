﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
#if !WINDOWS_PHONE && !NET35
using System.Dynamic;
#endif
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.AspNet.SignalR.Client.Hubs
{
    public class HubProxy :
#if !WINDOWS_PHONE && !NET35
 DynamicObject,
#endif
 IHubProxy
    {
        private readonly string _hubName;
        private readonly IHubConnection _connection;
        private readonly Dictionary<string, JToken> _state = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Subscription> _subscriptions = new Dictionary<string, Subscription>(StringComparer.OrdinalIgnoreCase);

        public HubProxy(IHubConnection connection, string hubName)
        {
            _connection = connection;
            _hubName = hubName;
        }

        public JToken this[string name]
        {
            get
            {
                lock (_state)
                {
                    JToken value;
                    _state.TryGetValue(name, out value);
                    return value;
                }
            }
            set
            {
                lock (_state)
                {
                    _state[name] = value;
                }
            }
        }

        public Subscription Subscribe(string eventName)
        {
            if (eventName == null)
            {
                throw new ArgumentNullException("eventName");
            }

            Subscription subscription;
            if (!_subscriptions.TryGetValue(eventName, out subscription))
            {
                subscription = new Subscription();
                _subscriptions.Add(eventName, subscription);
            }

            return subscription;
        }

        public Task Invoke(string method, params object[] args)
        {
            return Invoke<object>(method, args);
        }

        public Task<T> Invoke<T>(string method, params object[] args)
        {
            if (method == null)
            {
                throw new ArgumentNullException("method");
            }

            if (args == null)
            {
                throw new ArgumentNullException("args");
            }

            var tokenifiedArguments = new JToken[args.Length];
            for (int i = 0; i < tokenifiedArguments.Length; i++)
            {
                tokenifiedArguments[i] = JToken.FromObject(args[i]);
            }

            var tcs = new TaskCompletionSource<T>();
            var callbackId = _connection.RegisterCallback(result =>
            {
                if (result != null)
                {
                    if (result.Error != null)
                    {
                        tcs.TrySetException(new InvalidOperationException(result.Error));
                    }
                    else
                    {
                        if (result.State != null)
                        {
                            foreach (var pair in result.State)
                            {
                                this[pair.Key] = pair.Value;
                            }
                        }

                        if (result.Result != null)
                        {
                            tcs.TrySetResult(result.Result.ToObject<T>());
                        }
                        else
                        {
                            tcs.TrySetResult(default(T));
                        }
                    }
                }
                else
                {
                    tcs.TrySetResult(default(T));
                }
            });

            var hubData = new HubInvocation
            {
                Hub = _hubName,
                Method = method,
                Args = tokenifiedArguments,
                CallbackId = callbackId
            };

            if (_state.Count != 0)
            {
                hubData.State = _state;
            }

            var value = JsonConvert.SerializeObject(hubData);

            return _connection.Send(value).Then(() => tcs.Task);

        }

#if !WINDOWS_PHONE && !NET35
        [SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "The compiler generates calls to invoke this")]
        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            this[binder.Name] = value as JToken ?? JToken.FromObject(value);
            return true;
        }

        [SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "The compiler generates calls to invoke this")]
        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            result = this[binder.Name];
            return true;
        }

        [SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "The compiler generates calls to invoke this")]
        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            result = Invoke(binder.Name, args);
            return true;
        }
#endif

        public void InvokeEvent(string eventName, JToken[] args)
        {
            Subscription eventObj;
            if (_subscriptions.TryGetValue(eventName, out eventObj))
            {
                eventObj.OnData(args);
            }
        }
    }
}
