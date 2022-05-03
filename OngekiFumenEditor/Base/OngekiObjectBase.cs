using Caliburn.Micro;
using OngekiFumenEditor.Modules.FumenVisualEditor.ViewModels.OngekiObjects;
using OngekiFumenEditor.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OngekiFumenEditor.Base
{
    public abstract class OngekiObjectBase : PropertyChangedBase
    {
        public abstract string IDShortName { get; }
        public string Name => GetType().GetTypeName();

        public override string ToString() => IDShortName;

        private string tag = string.Empty;
        /// <summary>
        /// ��ʾ�û��Զ���ı�ǩ��һ�����ڽű�����
        /// </summary>
        public string Tag
        {
            get => tag;
            set => Set(ref tag, value);
        }

        /// <summary>
        /// �����������������
        /// </summary>
        /// <param name="fromObj">����Դ��������ķ���Ŀ��</param>
        public abstract void Copy(OngekiObjectBase fromObj, OngekiFumen fumen);
    }
}
