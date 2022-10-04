using Caliburn.Micro;
using OngekiFumenEditor.Base.Attributes;
using OngekiFumenEditor.Modules.FumenVisualEditor.ViewModels;
using OngekiFumenEditor.Modules.FumenVisualEditor.ViewModels.OngekiObjects;
using OngekiFumenEditor.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace OngekiFumenEditor.Base
{
    public abstract class OngekiObjectBase : PropertyChangedBase
    {
        private static int ID_GEN = 0;

        [ObjectPropertyBrowserReadOnly]
        public int Id { get; init; } = ID_GEN++;

        public abstract string IDShortName { get; }
        public string Name => GetType().GetTypeName();

        public override string ToString() => $"[oid:{Id}]{IDShortName}";

        [ObjectPropertyBrowserHide]
        public override bool IsNotifying
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => base.IsNotifying;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => base.IsNotifying = value;
        }

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

        public OngekiObjectBase CopyNew(OngekiFumen fumen)
        {
            if (this is not IDisplayableObject displayable
                //�ݲ�֧�� �������͵ĸ���ճ��
                //|| obj is ConnectableObjectBase
                )
                return default;

            var newObj = CacheLambdaActivator.CreateInstance(GetType()) as OngekiObjectBase;
            newObj.Copy(this, fumen);
            return newObj;
        }
    }
}
