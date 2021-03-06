﻿namespace tomenglertde.ResXManager.Translators
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.Composition;
    using System.Diagnostics;
    using System.Diagnostics.Contracts;
    using System.Threading;

    using JetBrains.Annotations;

    using Newtonsoft.Json;

    using tomenglertde.ResXManager.Infrastructure;
    using tomenglertde.ResXManager.Translators.Properties;

    using TomsToolbox.Core;
    using TomsToolbox.Desktop;

    [Export]
    public class TranslatorHost
    {
        [NotNull]
        private readonly Throttle _changeThrottle;
        [NotNull]
        private readonly ITranslator[] _translators;

        [ImportingConstructor]
        public TranslatorHost([ImportMany][NotNull][ItemNotNull] ITranslator[] translators)
        {
            Contract.Requires(translators != null);

            _changeThrottle = new Throttle(TimeSpan.FromSeconds(1), SaveConfiguration);
            _translators = translators;
            // ReSharper disable once PossibleNullReferenceException
            _translators.ForEach(translator => translator.PropertyChanged += (_, __) => _changeThrottle.Tick());

            var settings = Settings.Default;
            var configuration = settings.Configuration;

            if (string.IsNullOrEmpty(configuration))
                return;

            try
            {
                var values = JsonConvert.DeserializeObject<Dictionary<string, string>>(configuration);
                Contract.Assume(values != null);

                foreach (var translator in _translators)
                {
                    Contract.Assume(translator != null);

                    string setting;

                    if (!values.TryGetValue(translator.Id, out setting))
                        continue;
                    if (string.IsNullOrEmpty(setting))
                        continue;

                    try
                    {
                        JsonConvert.PopulateObject(setting, translator);
                    }
                    catch
                    {
                        // Newtonsoft.Jason has not documented any exceptions...
                    }
                }
            }
            catch
            {
                // Newtonsoft.Jason has not documented any exceptions...           
            }
        }

        [NotNull]
        public IEnumerable<ITranslator> Translators
        {
            get
            {
                Contract.Ensures(Contract.Result<IEnumerable<ITranslator>>() != null);

                return _translators;
            }
        }

        public void SaveConfiguration()
        {
            var settings = Settings.Default;

            var values = new Dictionary<string, string>();

            foreach (var translator in Translators)
            {
                Contract.Assume(translator != null);

                var json = JsonConvert.SerializeObject(translator);
                values[translator.Id] = json;
            }

            settings.Configuration = JsonConvert.SerializeObject(values);
        }

        public void Translate([NotNull] ITranslationSession translationSession)
        {
            Contract.Requires(translationSession != null);

            var translatorCounter = 0;

            foreach (var translator in Translators)
            {
                Contract.Assume(translator != null);

                var local = translator;
                if (!local.IsEnabled)
                    continue;

                Interlocked.Increment(ref translatorCounter);

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        local.Translate(translationSession);
                    }
                    finally
                    {
                        // ReSharper disable once AccessToModifiedClosure
                        if (Interlocked.Decrement(ref translatorCounter) == 0)
                        {
                            translationSession.IsComplete = true;
                        }
                    }
                });
            }

            if (translatorCounter == 0)
            {
                translationSession.IsComplete = true;
            }
        }

        [ContractInvariantMethod]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Required for code contracts.")]
        [Conditional("CONTRACTS_FULL")]
        private void ObjectInvariant()
        {
            Contract.Invariant(_changeThrottle != null);
            Contract.Invariant(_translators != null);
        }
    }
}
