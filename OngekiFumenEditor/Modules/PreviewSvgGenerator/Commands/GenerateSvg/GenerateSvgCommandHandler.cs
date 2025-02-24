﻿using Caliburn.Micro;
using Gemini.Framework.Commands;
using Gemini.Framework.Threading;
using OngekiFumenEditor.Modules.FumenVisualEditor.Kernel;
using OngekiFumenEditor.Modules.FumenVisualEditor.ViewModels;
using OngekiFumenEditor.Modules.PreviewSvgGenerator.Commands.GenerateSvg;
using OngekiFumenEditor.Utils;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace OngekiFumenEditor.Modules.PreviewSvgGenerator.Commands.GenerateSvg
{
    [CommandHandler]
    public class GenerateSvgCommandHandler : CommandHandlerBase<GenerateSvgCommandDefinition>
    {
        private readonly IEditorDocumentManager editorDocumentManager;

        [ImportingConstructor]
        public GenerateSvgCommandHandler(IEditorDocumentManager editorDocumentManager)
        {
            this.editorDocumentManager = editorDocumentManager;
        }

        public override void Update(Command command)
        {
            base.Update(command);
            command.Enabled = editorDocumentManager.CurrentActivatedEditor is not null;
        }

        public override async Task Run(Command command)
        {
            if (editorDocumentManager.CurrentActivatedEditor is FumenVisualEditorViewModel editor)
            {
                try
                {
                    var opt = new GenerateOption()
                    {
                        Duration = editor.EditorProjectData.AudioDuration,
                        OutputFilePath = Path.GetTempFileName() + ".svg"
                    };
                    await IoC.Get<IPreviewSvgGenerator>().GenerateSvgAsync(editor.Fumen, opt);
                    if (MessageBox.Show($"生成svg文件成功,是否立即打开文件?", "提示", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        ProcessUtils.OpenPath(opt.OutputFilePath);

                }
                catch (Exception e)
                {
                    MessageBox.Show($"生成svg文件失败:{e.Message}");
                }
            }
        }
    }
}