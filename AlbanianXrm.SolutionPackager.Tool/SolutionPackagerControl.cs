﻿using AlbanianXrm.SolutionPackager.Extensions;
using AlbanianXrm.SolutionPackager.Properties;
using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace AlbanianXrm.SolutionPackager
{
    internal partial class SolutionPackagerControl : PluginControlBase, IGitHubPlugin, INoConnectionRequired, IAboutPlugin
    {
        /// <summary>
        /// GitHub repository 
        /// </summary>
        public string RepositoryName => "solution-packager";

        /// <summary>
        /// GitHub UserName
        /// </summary>
        public string UserName => "albanian-xrm";

        public void ShowAboutDialog()
        {
            new SolutionPackagerAbout().ShowDialog(this);
        }

        private readonly AsyncWorkQueue asyncWorkQueue;
        private readonly PluginViewModel pluginViewModel;
        private readonly CoreToolsDownloader coreToolsDownloader;
        private readonly CrmSolutionManager crmSolutionManager;
        private readonly SolutionPackagerCaller solutionPackagerCaller;
        private readonly Type pluginType;

        private readonly string[] packageTypes = new string[] { null, "Unmanaged", "Managed", "Both" };
        private readonly string[] errorLevels = new string[] { null, "Off", "Error", "Warning", "Info", "Verbose" };
        private readonly string[] languageCodes = new string[] { null, "auto", "1033", "1025", "1069", "1026", "1027", "3076", "2052", "1028", "1050", "1029", "1030", "1043", "1061", "1035", "1036", "1110", "1031", "1032", "1037", "1081", "1038", "1057", "1040", "1041", "1087", "1042", "1062", "1063", "1086", "1044", "1045", "1046", "2070", "1048", "1049", "3098", "2074", "1051", "1060", "3082", "1053", "1054", "1055", "1058", "1066" };

        public SolutionPackagerControl(Type pluginType)
        {
            this.pluginType = pluginType;
            InitializeComponent();

            pluginViewModel = new PluginViewModel();
            asyncWorkQueue = new AsyncWorkQueue(this, pluginViewModel);
            coreToolsDownloader = new CoreToolsDownloader(asyncWorkQueue, pluginViewModel);
            solutionPackagerCaller = new SolutionPackagerCaller(this, asyncWorkQueue, txtOutput);
            crmSolutionManager = new CrmSolutionManager(this, asyncWorkQueue, solutionPackagerCaller, cmbCrmSolutions);

            localOrCrm.DataBindings.Add(nameof(localOrCrm.Enabled), pluginViewModel, nameof(pluginViewModel.HasConnection));
            grpExportSolution.DataBindings.Add(nameof(grpExportSolution.Visible), pluginViewModel, nameof(pluginViewModel.LocalOrCrm));

            btnRefreshSolutions.DataBindings.Add(nameof(btnRefreshSolutions.Enabled), pluginViewModel, nameof(pluginViewModel.LocalOrCrm));
            tabsExtractOrPack.DataBindings.Add(nameof(tabsExtractOrPack.Enabled), pluginViewModel, nameof(pluginViewModel.AllowRequests));

            txtCoreTools.DataBindings.Add(nameof(txtCoreTools.Text), pluginViewModel, nameof(pluginViewModel.SolutionPackagerVersion));

            pluginViewModel.PropertyChanged += PluginViewModel_PropertyChanged;

            cmbLanguage.Items.AddRange(new object[] { CultureInfo.GetCultureInfo("en"), CultureInfo.GetCultureInfo("it") });
            cmbLanguage.SelectedIndex = 0;
            cmbPackageTypeExtract.SelectedIndex = 0;
            cmbSourceLocaleExtract.SelectedIndex = 0;
            cmbErrorLevelExtract.SelectedIndex = 0;
            cmbPackageTypePack.SelectedIndex = 0;
            cmbErrorLevelPack.SelectedIndex = 0;
        }

        private void PluginViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(pluginViewModel.CultureInfo))
            {
                ComponentResourceManager resources = new ComponentResourceManager(typeof(SolutionPackagerControl));
                foreach (Control c in this.GetAllControls())
                {
                    resources.ApplyResources(c, c.Name, Resources.Culture);
                }
            }
        }

        private void SolutionPackagerControl_Load(object sender, EventArgs e)
        {
#pragma warning disable IDE0018 // Inline variable declaration
            Settings settings;
#pragma warning restore IDE0018 // Inline variable declaration
            // Loads or creates the settings for the plugin
            if (!SettingsManager.Instance.TryLoad(pluginType, out settings))
            {
                LogWarning(Resources.SETTINGS_NOT_FOUND);
                return;
            }
            pluginViewModel.Settings = settings;
            txtNuGetFeed.Text = settings.NugetFeed;
            var selection = cmbLanguage.Items.IndexOf(CultureInfo.GetCultureInfo(settings.Language));
            if (selection >= 0)
            {
                cmbLanguage.SelectedIndex = selection;
            }
            LogInfo(Resources.SETTINGS_FOUND);
        }

        /// <summary>
        /// Allows for the plugin to prevent the form from closing, or preform some action before closing
        /// By default, if the Form is being closed, or a close all or all except active is being called, it won't prompt the user to ensure they wanted to close
        /// </summary>
        /// <param name="info"></param>
        public override void ClosingPlugin(PluginCloseInfo info)
        {
            if (info.Silent ||
                info.FormReason != CloseReason.None ||
                info.ToolBoxReason == ToolBoxCloseReason.CloseAll ||
                info.ToolBoxReason == ToolBoxCloseReason.CloseAllExceptActive)
            {
                return;
            }

            info.Cancel = new SolutionPackagerDialog(Resources.QUESTION_CLOSE_TOOL, Resources.QUESTION,
                                new SolutionPackagerDialog.ButtonProperties { Text = Resources.BTN_NO, Result = DialogResult.No },
                                new SolutionPackagerDialog.ButtonProperties { Text = Resources.BTN_YES, Result = DialogResult.Yes }).ShowDialog(this) != DialogResult.Yes;
        }

        #region Extract
        private void LocalOrCrm_CheckedChanged(object sender, EventArgs e)
        {
            errorProvider.Clear();
            pluginViewModel.LocalOrCrm = localOrCrm.Checked;
        }

        private void BtnRefreshSolutions_Click(object sender, EventArgs e)
        {
            errorProvider.Clear();
            crmSolutionManager.RefreshSolutionList();
        }

        private void BtnOpenZip_Click(object sender, EventArgs e)
        {
            errorProvider.Clear();
            if (localOrCrm.Checked)
            {
                var result = selectFolder.ShowDialog();
                if (result == DialogResult.OK)
                {
                    txtInputExtract.Text = selectFolder.SelectedPath;
                }
            }
            else
            {
                openFile.Filter = Resources.FILTER_SOLUTION;
                var result = openFile.ShowDialog();
                if (result == DialogResult.OK)
                {
                    txtInputExtract.Text = openFile.FileName;
                }
            }
        }

        private void BtnOutputFolder_Click(object sender, EventArgs e)
        {
            var result = selectFolder.ShowDialog();
            if (result == DialogResult.OK)
            {
                txtOutputExtract.Text = selectFolder.SelectedPath;
            }
        }

        private void BtnExtractMapFile_Click(object sender, EventArgs e)
        {
            openFile.Filter = Resources.FILTER_XML;
            var result = openFile.ShowDialog();
            if (result == DialogResult.OK)
            {
                txtMapExtract.Text = openFile.FileName;
            }
            else if (txtMapExtract.Text != "" && FieldClearDialog() == DialogResult.Yes)
            {
                txtMapExtract.Text = "";
            }
        }

        private void BtnExtractLog_Click(object sender, EventArgs e)
        {
            saveFile.Filter = Resources.FILTER_TEXT;
            var result = saveFile.ShowDialog();
            if (result == DialogResult.OK)
            {
                txtLogExtract.Text = openFile.FileName;
            }
            else if (txtLogExtract.Text != "" && FieldClearDialog() == DialogResult.Yes)
            {
                txtLogExtract.Text = "";
            }
        }

        private void BtnExtractArguments_Click(object sender, EventArgs e)
        {
            openFile.Filter = Resources.FILTER_TEXT;
            var result = openFile.ShowDialog();
            if (result == DialogResult.OK)
            {
                txtArgumentsExtract.Text = openFile.FileName;
            }
            else if (txtArgumentsExtract.Text != "" && FieldClearDialog() == DialogResult.Yes)
            {
                txtArgumentsExtract.Text = "";
            }
        }

        private void BtnExtract_Click(object sender, EventArgs e)
        {
            int nrErrors = 0;
            if (txtOutputExtract.Text.Length == 0)
            {
                errorProvider.SetError(txtOutputExtract, Resources.OUTPUT_FOLDER_NOT_SPECIFIED);
                nrErrors += 1;
            }
            if (CoreToolsDownloader.GetSolutionPackagerVersion() == null)
            {
                errorProvider.SetError(localOrCrm, Resources.SOLUTIONPACKAGER_MISSING);
                nrErrors += 1;
            }
            if (localOrCrm.Checked)
            {
                if (cmbCrmSolutions.SelectedIndex < 0)
                {
                    errorProvider.SetError(cmbCrmSolutions, Resources.NO_SOLUTION_SELECTED);
                    nrErrors += 1;
                }
            }
            else
            {
                if (!File.Exists(txtInputExtract.Text))
                {
                    errorProvider.SetError(txtInputExtract, Resources.ZIP_FILE_DOES_NOT_EXIST);
                    nrErrors += 1;
                }
            }
            if (nrErrors > 0)
            {
                errorProvider.SetError(btnExtract, nrErrors == 1 ? Resources.EXPORT_ERROR : string.Format(CultureInfo.InvariantCulture, Resources.EXPORT_ERRORS, nrErrors));
                return;
            }

            var parameters = new SolutionPackagerCaller.Parameters()
            {
                Action = "Extract",
                ZipFile = txtInputExtract.Text,
                OutputFolder = txtOutputExtract.Text,
                PackageType = packageTypes[cmbPackageTypeExtract.SelectedIndex],
                AllowWrite = chkAllowWriteExtract.Checked,
                AllowDelete = radAllowDeleteYes.Checked ? true : (radAllowDeleteNo.Checked ? false : default(bool?)),
                Clobber = chkClobberExtract.Checked,
                ErrorLevel = errorLevels[cmbErrorLevelExtract.SelectedIndex],
                MapFile = txtMapExtract.Text,
                NoLogo = chkNoLogoExtract.Checked,
                LogFile = txtLogExtract.Text,
                Arguments = txtArgumentsExtract.Text,
                SourceLocale = languageCodes[cmbSourceLocaleExtract.SelectedIndex],
                Localize = chkLocalizeExtract.Checked,
                FormatXml = chkFormatDocumentExtract.Checked
            };

            if (localOrCrm.Checked)
            {
                crmSolutionManager.DownloadSolution(
                   new CrmSolutionManager.DownloadSolutionParams(
                       cmbCrmSolutions.SelectedItem as Models.Solution,
                       zipDirectory: txtInputExtract.Text,
                       solutionPackagerParameters: parameters,
                       managed: parameters.PackageType == "Both" ? new bool[] { true, false } : new bool[] { radManaged.Checked },
                       exportAutoNumberingSettings: chkExportAutoNumbering.Checked,
                       exportCalendarSettings: chkExportCalendar.Checked,
                       exportCustomizationSettings: chkExportCustomization.Checked,
                       exportEmailTrackingSettings: chkExportEmailTracking.Checked,
                       exportExternalApplications: chkExportExternalApplications.Checked,
                       exportGeneralSettings: chkExportGeneralSettings.Checked,
                       exportIsvConfig: chkExportISVConfig.Checked,
                       exportMarketingSettings: chkExportMarketing.Checked,
                       exportOutlookSynchronizationSettings: chkExportOutlookSynchronization.Checked,
                       exportRelationshipRoles: chkExportRelationshipRoles.Checked,
                       exportSales: chkExportSales.Checked));
            }
            else
            {
                solutionPackagerCaller.ManageSolution(parameters);
            }
        }
        #endregion

        #region Pack
        private void BtnInputFolder_Click(object sender, EventArgs e)
        {
            var result = selectFolder.ShowDialog();
            if (result == DialogResult.OK)
            {
                txtInputPack.Text = selectFolder.SelectedPath;
            }
        }

        private void BtnOutputZip_Click(object sender, EventArgs e)
        {
            saveFile.Filter = Resources.FILTER_SOLUTION;
            var result = saveFile.ShowDialog();
            if (result == DialogResult.OK)
            {
                txtOutputPack.Text = saveFile.FileName;
            }
        }

        private void BtnPackMap_Click(object sender, EventArgs e)
        {
            openFile.Filter = Resources.FILTER_XML;
            var result = openFile.ShowDialog();
            if (result == DialogResult.OK)
            {
                txtMapPack.Text = openFile.FileName;
            }
            else if (txtMapPack.Text != "" && FieldClearDialog() == DialogResult.Yes)
            {
                txtMapPack.Text = "";
            }
        }

        private void BtnLogPack_Click(object sender, EventArgs e)
        {
            saveFile.Filter = Resources.FILTER_TEXT;
            var result = saveFile.ShowDialog();
            if (result == DialogResult.OK)
            {
                txtLogPack.Text = saveFile.FileName;
            }
            else if (txtLogPack.Text != "" && FieldClearDialog() == DialogResult.Yes)
            {
                txtLogPack.Text = "";
            }
        }

        private void BtnArgumentsPack_Click(object sender, EventArgs e)
        {
            openFile.Filter = Resources.FILTER_TEXT;
            var result = openFile.ShowDialog();
            if (result == DialogResult.OK)
            {
                txtArgumentsPack.Text = openFile.FileName;
            }
            else if (txtArgumentsPack.Text != "" && FieldClearDialog() == DialogResult.Yes)
            {
                txtArgumentsPack.Text = "";
            }
        }

        private void btnPack_Click(object sender, EventArgs e)
        {
            int nrErrors = 0;
            if (txtInputPack.Text.Length == 0)
            {
                errorProvider.SetError(txtInputPack, Resources.INPUT_FOLDER_NOT_SPECIFIED);
                nrErrors += 1;
            }
            if (CoreToolsDownloader.GetSolutionPackagerVersion() == null)
            {
                errorProvider.SetError(localOrCrm, Resources.SOLUTIONPACKAGER_MISSING);
                nrErrors += 1;
            }
            if (txtOutputPack.Text.Length == 0)
            {
                errorProvider.SetError(txtOutputPack, Resources.ZIP_FILE_NOT_SPECIFIED);
                nrErrors += 1;
            }

            if (nrErrors > 0)
            {
                errorProvider.SetError(btnPack, nrErrors == 1 ? Resources.EXPORT_ERROR : string.Format(CultureInfo.InvariantCulture, Resources.EXPORT_ERRORS, nrErrors));
                return;
            }

            var parameters = new SolutionPackagerCaller.Parameters()
            {
                Action = "Pack",
                ZipFile = txtOutputPack.Text,
                OutputFolder = txtInputPack.Text,
                PackageType = packageTypes[cmbPackageTypePack.SelectedIndex],
                ErrorLevel = errorLevels[cmbErrorLevelPack.SelectedIndex],
                MapFile = txtMapPack.Text,
                NoLogo = chkNoLogoPack.Checked,
                LogFile = txtLogPack.Text,
                Arguments = txtArgumentsPack.Text
            };


            solutionPackagerCaller.ManageSolution(parameters);
        }
        #endregion

        #region Tool Events
        /// <summary>
        /// This event occurs when the connection has been updated in XrmToolBox
        /// </summary>
        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            base.UpdateConnection(newService, detail, actionName, parameter);
            pluginViewModel.HasConnection = detail != null;
        }
        #endregion

        #region Settings
        private void BtnCoreTools_Click(object sender, EventArgs e)
        {
            coreToolsDownloader.DownloadCoreTools(txtNuGetFeed.Text);
        }

        private void BtnNuGetFeed_Click(object sender, EventArgs e)
        {
            txtNuGetFeed.Text = "https://packages.nuget.org/api/v2";
        }

        private void BtnSaveSettings_Click(object sender, EventArgs e)
        {
            pluginViewModel.Settings.NugetFeed = txtNuGetFeed.Text;
            LogInfo(Resources.SETTINGS_SAVING);
            SettingsManager.Instance.Save(pluginType, pluginViewModel.Settings);
        }

        private void CmbLanguage_SelectedIndexChanged(object sender, EventArgs e)
        {
            pluginViewModel.CultureInfo = cmbLanguage.Items[cmbLanguage.SelectedIndex] as CultureInfo;
        }
        #endregion

        private void TabsExtractOrPack_Selected(object sender, TabControlEventArgs e)
        {
            if (e.Action == TabControlAction.Selected && e.TabPage == tabSettings)
            {
                pluginViewModel.SolutionPackagerVersion = CoreToolsDownloader.GetSolutionPackagerVersion()?.ToString();
            }
        }

        private DialogResult FieldClearDialog()
        {
            using (var dialog = new SolutionPackagerDialog(Resources.FIELD_CLEAR_MESSAGE,
                                                           Resources.FIELD_CLEAR_TITLE,
                                                           new SolutionPackagerDialog.ButtonProperties() { Text=Resources.BTN_NO, Result = DialogResult.No },
                                                           new SolutionPackagerDialog.ButtonProperties() { Text=Resources.BTN_YES, Result = DialogResult.Yes }))
            {
                return dialog.ShowDialog(this);
            }
        }
    }
}
