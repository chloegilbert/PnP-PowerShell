﻿using System;
using System.IO;
using System.Management.Automation;
using Microsoft.SharePoint.Client;
using OfficeDevPnP.Core.Framework.Provisioning.Model;
using SharePointPnP.PowerShell.CmdletHelpAttributes;
using OfficeDevPnP.Core.Framework.Provisioning.Providers.Xml;
using OfficeDevPnP.Core.Framework.Provisioning.Connectors;
using OfficeDevPnP.Core.Framework.Provisioning.ObjectHandlers;
using System.Collections;
using System.Linq;
using OfficeDevPnP.Core.Framework.Provisioning.Providers;

namespace SharePointPnP.PowerShell.Commands.Branding
{
    [Cmdlet("Apply", "SPOProvisioningTemplate")]
    [CmdletHelp("Applies a provisioning template to a web",
        Category = CmdletHelpCategory.Branding)]
    [CmdletExample(
     Code = @"PS:> Apply-SPOProvisioningTemplate -Path template.xml",
     Remarks = @"Applies a provisioning template in XML format to the current web.
",
     SortOrder = 1)]
    [CmdletExample(
     Code = @"PS:> Apply-SPOProvisioningTemplate -Path template.xml -ResourceFolder c:\provisioning\resources",
     Remarks = @"Applies a provisioning template in XML format to the current web. Any resources like files that are referenced in the template will be retrieved from the folder as specified with the ResourceFolder parameter.
",
     SortOrder = 2)]

    [CmdletExample(
     Code = @"PS:> Apply-SPOProvisioningTemplate -Path template.xml -Parameters @{""ListTitle""=""Projects"";""parameter2""=""a second value""}",
     Remarks = @"Applies a provisioning template in XML format to the current web. It will populate the parameter in the template the values as specified and in the template you can refer to those values with the {parameter:<key>} token.

For instance with the example above, specifying {parameter:ListTitle} in your template will translate to 'Projects' when applying the template. These tokens can be used in most string values in a template.",
     SortOrder = 3)]

    [CmdletExample(
     Code = @"PS:> Apply-SPOProvisioningTemplate -Path template.xml -Handlers Lists, SiteSecurity",
     Remarks = @"Applies a provisioning template in XML format to the current web. It will only apply the lists and site security part of the template.",
     SortOrder = 4)]

    [CmdletExample(
     Code = @"PS:> Apply-SPOProvisioningTemplate -Path template.pnp",
     Remarks = @"Applies a provisioning template from a pnp package to the current web.",
     SortOrder = 5)]

    [CmdletExample(
     Code = @"PS:> Apply-SPOProvisioningTemplate -Path https://tenant.sharepoint.com/sites/templatestorage/Documents/template.pnp",
     Remarks = @"Applies a provisioning template from a pnp package stored in a library to the current web.",
     SortOrder = 6)]

    [CmdletExample(
        Code = @"
PS:> $handler1 = New-SPOExtensibilityHandlerObject -Assembly Contoso.Core.Handlers -Type Contoso.Core.Handlers.MyExtensibilityHandler1
PS:> $handler2 = New-SPOExtensibilityHandlerObject -Assembly Contoso.Core.Handlers -Type Contoso.Core.Handlers.MyExtensibilityHandler1
PS:> Apply-SPOProvisioningTemplate -Path NewTemplate.xml -ExtensibilityHandlers $handler1,$handler2",
        Remarks = @"This will create two new ExtensibilityHandler objects that are run while provisioning the template",
        SortOrder = 7)]

    public class ApplyProvisioningTemplate : SPOWebCmdlet
    {
        [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true, ValueFromPipeline = true, HelpMessage = "Path to the xml or pnp file containing the provisioning template.")]
        public string Path;

        [Parameter(Mandatory = false, HelpMessage = "Root folder where resources/files that are being referenced in the template are located. If not specified the same folder as where the provisioning template is located will be used.")]
        public string ResourceFolder;

        [Parameter(Mandatory = false, HelpMessage = "Specify this parameter if you want to overwrite and/or create properties that are known to be system entries (starting with vti_, dlc_, etc.)")]
        public SwitchParameter OverwriteSystemPropertyBagValues;

        [Parameter(Mandatory = false, HelpMessage = "Allows you to specify parameters that can be referred to in the template by means of the {parameter:<Key>} token. See examples on how to use this parameter.")]
        public Hashtable Parameters;

        [Parameter(Mandatory = false, HelpMessage = "Allows you to only process a specific part of the template. Notice that this might fail, as some of the handlers require other artifacts in place if they are not part of what your applying.")]
        public Handlers Handlers;

        [Parameter(Mandatory = false, HelpMessage = "Allows you to run all handlers, excluding the ones specified.")]
        public Handlers ExcludeHandlers;

        [Parameter(Mandatory = false, HelpMessage = "Allows you to specify ExtensbilityHandlers to execute while applying a template")]
        public ExtensibilityHandler[] ExtensibilityHandlers;

        [Parameter(Mandatory = false, HelpMessage = "Allows you to specify ITemplateProviderExtension to execute while applying a template.")]
        public ITemplateProviderExtension[] TemplateProviderExtensions;

        protected override void ExecuteCmdlet()
        {
            SelectedWeb.EnsureProperty(w => w.Url);
            bool templateFromFileSystem = !Path.ToLower().StartsWith("http");
            FileConnectorBase fileConnector;
            string templateFileName = System.IO.Path.GetFileName(Path);
            if (templateFromFileSystem)
            {
                if (!System.IO.Path.IsPathRooted(Path))
                {
                    Path = System.IO.Path.Combine(SessionState.Path.CurrentFileSystemLocation.Path, Path);
                }
                if (!string.IsNullOrEmpty(ResourceFolder))
                {
                    if (!System.IO.Path.IsPathRooted(ResourceFolder))
                    {
                        ResourceFolder = System.IO.Path.Combine(SessionState.Path.CurrentFileSystemLocation.Path,
                            ResourceFolder);
                    }
                }
                FileInfo fileInfo = new FileInfo(Path);
                fileConnector = new FileSystemConnector(fileInfo.DirectoryName, "");
            }
            else
            {                
                Uri fileUri = new Uri(Path);
                var webUrl = Microsoft.SharePoint.Client.Web.WebUrlFromFolderUrlDirect(this.ClientContext, fileUri);
                var templateContext = this.ClientContext.Clone(webUrl.ToString());

                string library = Path.ToLower().Replace(templateContext.Url.ToLower(), "").TrimStart('/');
                int idx = library.IndexOf("/");
                library = library.Substring(0, idx);
                fileConnector = new SharePointConnector(templateContext, templateContext.Url, library);
            }
            XMLTemplateProvider provider = null;
            ProvisioningTemplate provisioningTemplate = null;
            Stream stream = fileConnector.GetFileStream(templateFileName);
            var isOpenOfficeFile = IsOpenOfficeFile(stream);
            if (isOpenOfficeFile)
            {
                provider = new XMLOpenXMLTemplateProvider(new OpenXMLConnector(templateFileName, fileConnector));
                templateFileName = templateFileName.Substring(0, templateFileName.LastIndexOf(".")) + ".xml";
            }
            else
            {
                if (templateFromFileSystem)
                {
                    provider = new XMLFileSystemTemplateProvider(fileConnector.Parameters[FileConnectorBase.CONNECTIONSTRING] + "", "");
                }
                else
                {
                    throw new NotSupportedException("Only .pnp package files are supported from a SharePoint library");
                }
            }
            provisioningTemplate = provider.GetTemplate(templateFileName, TemplateProviderExtensions);

            if (provisioningTemplate == null) return;

            if (isOpenOfficeFile)
            {
                provisioningTemplate.Connector = provider.Connector;
            }
            else
            {
                if (ResourceFolder != null)
                {
                    var fileSystemConnector = new FileSystemConnector(ResourceFolder, "");
                    provisioningTemplate.Connector = fileSystemConnector;
                }
                else
                {
                    provisioningTemplate.Connector = provider.Connector;
                }
            }

            if (Parameters != null)
            {
                foreach (var parameter in Parameters.Keys)
                {
                    if (provisioningTemplate.Parameters.ContainsKey(parameter.ToString()))
                    {
                        provisioningTemplate.Parameters[parameter.ToString()] = Parameters[parameter].ToString();
                    }
                    else
                    {
                        provisioningTemplate.Parameters.Add(parameter.ToString(), Parameters[parameter].ToString());
                    }
                }
            }

            var applyingInformation = new ProvisioningTemplateApplyingInformation();

            if (this.MyInvocation.BoundParameters.ContainsKey("Handlers"))
            {
                applyingInformation.HandlersToProcess = Handlers;
            }
            if (this.MyInvocation.BoundParameters.ContainsKey("ExcludeHandlers"))
            {
                foreach (var handler in (OfficeDevPnP.Core.Framework.Provisioning.Model.Handlers[])Enum.GetValues(typeof(Handlers)))
                {
                    if (!ExcludeHandlers.Has(handler) && handler != Handlers.All)
                    {
                        Handlers = Handlers | handler;
                    }
                }
                applyingInformation.HandlersToProcess = Handlers;
            }

                if (ExtensibilityHandlers != null)
                {
                    applyingInformation.ExtensibilityHandlers = ExtensibilityHandlers.ToList<ExtensibilityHandler>();
                }

                applyingInformation.ProgressDelegate = (message, step, total) =>
                {
                    WriteProgress(new ProgressRecord(0, string.Format("Applying template to {0}", SelectedWeb.Url), message) { PercentComplete = (100 / total) * step });
                };

            applyingInformation.MessagesDelegate = (message, type) =>
            {
                if (type == ProvisioningMessageType.Warning)
                {
                    WriteWarning(message);
                }
            };

            applyingInformation.OverwriteSystemPropertyBagValues = OverwriteSystemPropertyBagValues;
            SelectedWeb.ApplyProvisioningTemplate(provisioningTemplate, applyingInformation);
        }

        private bool IsOpenOfficeFile(Stream stream)
        {
            bool istrue = false;
            // SIG 50 4B 03 04 14 00

            byte[] bytes = new byte[6];
            int n = stream.Read(bytes, 0, 6);
            var signature = string.Empty;
            foreach (var b in bytes)
            {
                signature += b.ToString("X2");
            }
            if (signature == "504B03041400")
            {
                istrue = true;
            }
            return istrue;
        }
    }
}
