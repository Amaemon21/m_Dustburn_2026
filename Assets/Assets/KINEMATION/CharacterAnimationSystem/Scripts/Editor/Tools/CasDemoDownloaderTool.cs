// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Editor.Tools;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor.Tools
{
    public class CasDemoDownloaderTool : DemoDownloaderTool
    {
        protected override string GetPackageUrl()
        {
            return "https://github.com/kinemation/demoes/releases/download/cas/CAS_Demo.unitypackage";
        }

        protected override string GetPackageFileName()
        {
            return "CasDemoProject";
        }

        public override string GetToolCategory()
        {
            return base.GetToolCategory() + "CAS/";
        }

        public override string GetToolName()
        {
            return "General";
        }

        public override string GetToolDescription()
        {
            return "Animations, scripts and prefabs for Character Animation System.";
        }

        public override string GetDocsURL()
        {
            return "https://kinemation.gitbook.io/character-animation-system-docs/quickstart/introduction-to-cas/install-demo-content";
        }

        protected override List<ContentLicense> GetContentLicenses()
        {
            return new List<ContentLicense>()
            {
                new ContentLicense()
                {
                    contentAuthor = "Mixamo",
                    contentName = "Animations",
                    tags = new List<Tag>()
                    {
                        new Tag("Adobe License", "Adobe", "https://www.adobe.com/legal/terms.html#")
                    }
                },
                new ContentLicense()
                {
                    contentAuthor = "Piloto Studios",
                    contentName = "Free Torch Pack",
                    tags = new List<Tag>()
                    {
                        new Tag("Asset Store EULA", "", "https://unity.com/legal/as-terms")
                    }
                }
            };
        }
    }
}