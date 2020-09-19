﻿using System;
using System.Xml;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
using IServiceProvider = System.IServiceProvider;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using System.Threading;

namespace FineCodeCoverage.Impl
{
	[Export(typeof(TestContainerDiscoverer))]
	[Export(typeof(ITestContainerDiscoverer))]
	[Name(Vsix.TestContainerDiscovererName)]
	internal class TestContainerDiscoverer : ITestContainerDiscoverer
	{
		public event EventHandler TestContainersUpdated;

		public Uri ExecutorUri => new Uri($"executor://{Vsix.Code}.Executor/v1");

		public IEnumerable<ITestContainer> TestContainers => Enumerable.Empty<ITestContainer>();

		[ImportingConstructor]
		internal TestContainerDiscoverer
		(
			[Import(typeof(IOperationState))]
			IOperationState operationState,

			[Import(typeof(SVsServiceProvider))]
			IServiceProvider serviceProvider
		)
		{
			try
			{
				Logger.Clear();
				Logger.Initialize(serviceProvider, Vsix.Name);

				if (CoverageUtil.CurrentCoverletVersion == null)
				{
					CoverageUtil.InstallCoverlet();
				}
				else if (CoverageUtil.CurrentCoverletVersion < CoverageUtil.MimimumCoverletVersion)
				{
					CoverageUtil.UpdateCoverlet();
				}

				TestContainersUpdated?.ToString();
				operationState.StateChanged += OperationState_StateChanged;
				
				Logger.Log
				(
					"Initialized",
					$"Version            {GetVersion()}",
					$"Coverlet Version   {CoverageUtil.CurrentCoverletVersion}",
					$"Work Directory     {CoverageUtil.AppDataFolder}",
					$"Coverlet Directory {CoverageUtil.AppDataCoverletFolder}"
				);
			}
			catch (Exception exception)
			{
				Logger.Log($"Failed Initialization", exception.ToString());
			}
		}

		private static string GetVersion()
		{
			try
			{
				var doc = new XmlDocument();
				doc.Load("./extension.vsixmanifest");
				var metaData = doc.DocumentElement.ChildNodes.Cast<XmlElement>().First(x => x.Name == "Metadata");
				var identity = metaData.ChildNodes.Cast<XmlElement>().First(x => x.Name == "Identity");
				var version = identity.GetAttribute("Version");
				return version;
			}
			catch
			{
				return "-";
			}
		}

		private void OperationState_StateChanged(object sender, OperationStateChangedEventArgs e)
		{
			try
			{
				if (e.State == TestOperationStates.TestExecutionFinished)
				{
					Logger.Clear();
					Logger.Log("Updating coverage ...");

					var operationType = e.Operation.GetType();
					var testConfiguration = (operationType.GetProperty("Configuration") ?? operationType.GetProperty("Configuration", BindingFlags.Instance | BindingFlags.NonPublic)).GetValue(e.Operation);
					var testConfigurationSources = (IEnumerable<string>)testConfiguration.GetType().GetProperty("TestSources").GetValue(testConfiguration);

					foreach (var testDllFile in testConfigurationSources)
					{
						CoverageUtil.LoadCoverageFromTestDllFile(testDllFile, exception =>
						{
							if (exception != null)
							{
								Logger.Log("Error updating coverage", exception);
								return;
							}

							TaggerProvider.ReloadTags();
							Logger.Log("Coverage updated!");
						});
					}
				}
			}
			catch (Exception exception)
			{
				Logger.Log("Error processing unit test events", exception);
			}
		}
	}
}
