﻿using Kbg.NppPluginNET.PluginInfrastructure;
using SHDocVw;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NppMarkdownPanel.Forms
{
    public partial class MarkdownPreviewForm : Form
    {
        const string DEFAULT_HTML_BASE =
         @"<!DOCTYPE html>
            <html>
                <head>
                    <title>{0}</title>
	                <meta http-equiv=""X-UA-Compatible"" content=""IE=edge""></meta>
	                <meta http-equiv=""content-type"" content=""text/html; charset=utf-8"">
                    <style type=""text/css"">
                    {1}
                    </style>
                </head>
                <body style=""{2}"">
                {3}
                </body>
            <html>
            ";

        private Task<Tuple<string, string>> renderTask;
        private readonly Action toolWindowCloseAction;
        private int lastVerticalScroll = 0;
        private string htmlContent;
        private bool showToolbar;

        public string CssFileName { get; set; }
        public int ZoomLevel { get; set; }
        public string HtmlFileName { get; set; }
        public bool ShowToolbar {
            get => showToolbar;
            set {
                showToolbar = value;
                tbPreview.Visible = value;
            }
        }

        private IMarkdownGenerator markdownGenerator;
        
        public MarkdownPreviewForm(Action toolWindowCloseAction)
        {
            this.toolWindowCloseAction = toolWindowCloseAction;
            InitializeComponent();
            markdownGenerator = MarkdownPanelController.GetMarkdownGeneratorImpl();
        }

        private Tuple<string,string> RenderHtmlInternal(string currentText, string filepath)
        {
            var result = markdownGenerator.ConvertToHtml(currentText, filepath);
            var resultWithRelativeImages = markdownGenerator.ConvertToHtml(currentText, null);
            var defaultBodyStyle = "";

            // Path of plugin directory
            var markdownStyleContent = "";

            var assemblyPath = Path.GetDirectoryName(Assembly.GetAssembly(GetType()).Location);

            if (File.Exists(CssFileName))
            {
                markdownStyleContent = File.ReadAllText(CssFileName);
            }
            else
            {
                markdownStyleContent = File.ReadAllText(assemblyPath + "\\" + MainResources.DefaultCssFile);
            }

            var markdownHtml = string.Format(DEFAULT_HTML_BASE, Path.GetFileName(filepath), markdownStyleContent, defaultBodyStyle, result);
            var markdownHtml2 = string.Format(DEFAULT_HTML_BASE, Path.GetFileName(filepath), markdownStyleContent, defaultBodyStyle, resultWithRelativeImages);
            return new Tuple<string, string>(markdownHtml, markdownHtml2);
        }

        public void RenderMarkdown(string currentText, string filepath)
        {
            if (renderTask == null || renderTask.IsCompleted)
            {
                SaveLastVerticalScrollPosition();
                MakeAndDisplayScreenShot();

                var context = TaskScheduler.FromCurrentSynchronizationContext();
                renderTask = new Task<Tuple<string, string>>(() => RenderHtmlInternal(currentText, filepath));
                renderTask.ContinueWith((renderedText) =>
                {
                    webBrowserPreview.DocumentText = renderedText.Result.Item1;
                    htmlContent = renderedText.Result.Item2;
                    if (!String.IsNullOrWhiteSpace(HtmlFileName))
                    {
                        bool valid = Utils.ValidateFileSelection(HtmlFileName, out string fullPath, out string error, "HTML Output");
                        if (valid)
                        {
                            HtmlFileName = fullPath; // the validation was run against this path, so we want to make sure the state of the preview matches that
                            try
                            {
                                File.WriteAllText(HtmlFileName, htmlContent);
                            }
                            catch (Exception)
                            {
                                // If it fails, just continue
                            }
                        }
                    }
                    AdjustZoomLevel();
                }, context);
                renderTask.Start();
            }
        }
        /// <summary>
        /// Makes and displays a screenshot of the current browser content to prevent it from flickering 
        /// while loading updated content
        /// </summary>
        private void MakeAndDisplayScreenShot()
        {
            Bitmap screenshot = new Bitmap(webBrowserPreview.Width, webBrowserPreview.Height);
            ActiveXScreenShotMaker.GetImage(webBrowserPreview.ActiveXInstance, screenshot, Color.White);
            pictureBoxScreenshot.Image = screenshot;
            pictureBoxScreenshot.Visible = true;
        }

        /// <summary>
        /// Saves the last vertical scrollpositions, after reloading the position will be 0
        /// </summary>
        private void SaveLastVerticalScrollPosition()
        {
            if (webBrowserPreview.Document != null)
            {
                try
                {
                    lastVerticalScroll = webBrowserPreview.Document.GetElementsByTagName("HTML")[0].ScrollTop;
                }
                catch { }
            }
        }

        private void webBrowserPreview_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            Cursor.Current = Cursors.IBeam;
            GoToLastVerticalScrollPosition();
            HideScreenshotAndShowBrowser();
        }

        private void GoToLastVerticalScrollPosition()
        {
            webBrowserPreview.Document.Window.ScrollTo(0, lastVerticalScroll);
            Application.DoEvents();
        }

        private void HideScreenshotAndShowBrowser()
        {
            pictureBoxScreenshot.Visible = false;
            pictureBoxScreenshot.Image = null;
        }

        public void ScrollToElementWithLineNo(int lineNo)
        {
            Application.DoEvents();
            if (webBrowserPreview.Document != null)
            {
                HtmlElement child = null;

                for (int i = lineNo; i >= 0; i--)
                {
                    var htmlElement = webBrowserPreview.Document.GetElementById(i.ToString());
                    if (htmlElement != null)
                    {
                        child = htmlElement;
                        break;
                    }
                }

                if (child != null)
                    webBrowserPreview.Document.Window.ScrollTo(0, CalculateAbsoluteYOffset(child) - 20);
            }


            //if (elementIndexesForAllLevels != null && elementIndexesForAllLevels.Count > 0 && webBrowserPreview.Document != null && webBrowserPreview.Document.Body != null /*&& webBrowserPreview.Document.Body.Children.Count > elementIndex - 1*/)
            //{
            //    HtmlElement currentElement = webBrowserPreview.Document.Body;
            //    HtmlElement child = null;
            //    foreach (int elementIndexForCurrentLevel in elementIndexesForAllLevels)
            //    {
            //        if (currentElement.Children.Count > elementIndexForCurrentLevel)
            //        {
            //            child = currentElement.Children[elementIndexForCurrentLevel];
            //            currentElement = child;
            //        }
            //        else
            //        {
            //            break;
            //        }
            //    }

            //    if (child != null)
            //        webBrowserPreview.Document.Window.ScrollTo(0, CalculateAbsoluteYOffset(child) - 20);
            //}
        }

        private int CalculateAbsoluteYOffset(HtmlElement currentElement)
        {
            int baseY = currentElement.OffsetRectangle.Top;
            var offsetParent = currentElement.OffsetParent;
            while (offsetParent != null)
            {
                baseY += offsetParent.OffsetRectangle.Top;
                offsetParent = offsetParent.OffsetParent;
            }

            return baseY;
        }

        /// <summary>
        /// Increase Zoomlevel in case of higher DPI settings
        /// </summary>
        private void AdjustZoomLevel()
        {
            Application.DoEvents();

            var browserInst = ((SHDocVw.IWebBrowser2)(webBrowserPreview.ActiveXInstance));
            browserInst.ExecWB(OLECMDID.OLECMDID_OPTICAL_ZOOM, OLECMDEXECOPT.OLECMDEXECOPT_DONTPROMPTUSER, ZoomLevel, IntPtr.Zero);
            //   webBrowserPreview.Document.Window.ScrollTo(0, 0);

        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NMHDR
        {
            public IntPtr hwndFrom;
            public IntPtr idFrom;
            public int code;
        }

        public enum WindowsMessage
        {
            WM_NOTIFY = 0x004E
        }

        protected override void WndProc(ref Message m)
        {
            //Listen for the closing of the dockable panel to toggle the toolbar icon
            switch (m.Msg)
            {
                case (int)WindowsMessage.WM_NOTIFY:
                    var notify = (NMHDR)Marshal.PtrToStructure(m.LParam, typeof(NMHDR));
                    if (notify.code == (int)DockMgrMsg.DMN_CLOSE)
                    {
                        toolWindowCloseAction();
                    }
                    break;
            }
            //Continue the processing, as we only toggle
            base.WndProc(ref m);
        }

        private void webBrowserPreview_Navigating(object sender, WebBrowserNavigatingEventArgs e)
        {
            if (e.Url.ToString() != "about:blank")
            {
                e.Cancel = true;
                var p = new Process();
                p.StartInfo = new ProcessStartInfo(e.Url.ToString());
                p.Start();
            }
        }

        private async void btnSaveHtml_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                Stream myStream;
                saveFileDialog.Filter = "html files (*.html, *.htm)|*.html;*.htm|All files (*.*)|*.*";
                saveFileDialog.RestoreDirectory = true;
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    if ((myStream = saveFileDialog.OpenFile()) != null)
                    {
                        await myStream.WriteAsync(Encoding.ASCII.GetBytes(htmlContent), 0, htmlContent.Length);
                        myStream.Close();
                    }
                }
            }
        }
    }
}
