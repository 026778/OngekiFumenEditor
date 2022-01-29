using Caliburn.Micro;
using OngekiFumenEditor.Modules.FumenVisualEditor.ViewModels.OngekiObjects;
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

        public override string ToString() => IDShortName;

        /// <summary>
        /// �����������������
        /// </summary>
        /// <param name="fromObj">����Դ��������ķ���Ŀ��</param>
        public abstract void Copy(OngekiObjectBase fromObj, OngekiFumen fumen);
    }
}
