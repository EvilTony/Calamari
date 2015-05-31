﻿using System.IO;
using Calamari.Deployment;
using Calamari.Tests.Fixtures.Deployment.Azure;
using Calamari.Tests.Helpers;
using NUnit.Framework;
using Octostache;

namespace Calamari.Tests.Fixtures.PowerShell
{
    [TestFixture]
    [Category(TestEnvironment.CompatableOS.Windows)]
    public class AzurePowershellFixture : CalamariFixture
    {
        [Test]
        public void ShouldSetAzureSubscription()
        {
            var variablesFile = Path.GetTempFileName();
            var variables = new VariableDictionary();
            variables.Set(SpecialVariables.Account.AccountType, "AzureSubscription");
            variables.Set(SpecialVariables.Account.Name, "AzureTest");
            variables.Save(variablesFile);
            OctopusTestAzureSubscription.PopulateVariables(variables);

            var output = Invoke(Calamari()
                .Action("run-script")
                .Argument("script", GetFixtureResouce("Scripts", "AzureSubscription.ps1"))
                .Argument("variables", variablesFile));

            output.AssertZero();
            output.AssertOutput("Current subscription ID: " + OctopusTestAzureSubscription.AzureSubscriptionId);
        }
    }
}