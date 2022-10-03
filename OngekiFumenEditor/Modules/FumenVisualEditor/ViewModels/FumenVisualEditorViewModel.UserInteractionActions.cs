using Caliburn.Micro;
using Gemini.Framework;
using Gemini.Modules.Toolbox;
using Gemini.Modules.Toolbox.Models;
using OngekiFumenEditor.Base;
using OngekiFumenEditor.Base.OngekiObjects.ConnectableObject;
using OngekiFumenEditor.Modules.AudioPlayerToolViewer;
using OngekiFumenEditor.Modules.FumenObjectPropertyBrowser;
using OngekiFumenEditor.Modules.FumenVisualEditor.Base;
using OngekiFumenEditor.Modules.FumenVisualEditor.Base.DropActions;
using OngekiFumenEditor.Modules.FumenVisualEditor.Kernel;
using OngekiFumenEditor.Modules.FumenVisualEditor.Views;
using OngekiFumenEditor.Modules.FumenVisualEditor.Views.UI;
using OngekiFumenEditor.Utils;
using OngekiFumenEditor.Utils.ObjectPool;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OngekiFumenEditor.Modules.FumenVisualEditor.ViewModels
{
    public partial class FumenVisualEditorViewModel : PersistedDocument
    {
        #region Visibilities

        private bool isLocked = default;
        public bool IsLocked
        {
            get => isLocked;
            set
            {
                Set(ref isLocked, value);
                NotifyOfPropertyChange(() => EditorObjectVisibility);
                NotifyOfPropertyChange(() => EditorLockedVisibility);
            }
        }

        private bool isUserRequestHideEditorObject = default;
        public bool IsUserRequestHideEditorObject
        {
            get => isUserRequestHideEditorObject;
            set
            {
                Set(ref isUserRequestHideEditorObject, value);
                NotifyOfPropertyChange(() => EditorObjectVisibility);
            }
        }

        public Visibility EditorLockedVisibility =>
            IsLocked
            ? Visibility.Hidden : Visibility.Visible;

        public Visibility EditorObjectVisibility =>
            IsLocked || // �༭������ס
            IsUserRequestHideEditorObject // �û�Ҫ������(���簴��Q)
            ? Visibility.Hidden : Visibility.Visible;

        #endregion

        #region Selection

        private Visibility selectionVisibility;
        public Visibility SelectionVisibility
        {
            get => selectionVisibility;
            set => Set(ref selectionVisibility, value);
        }

        private Point selectionStartPosition;
        public Point SelectionStartPosition
        {
            get => selectionStartPosition;
            set => Set(ref selectionStartPosition, value);
        }

        private Point selectionCurrentCursorPosition;
        public Point SelectionCurrentCursorPosition
        {
            get => selectionCurrentCursorPosition;
            set
            {
                Set(ref selectionCurrentCursorPosition, value);
                RecalculateSelectionRect();
            }
        }

        private Rect selectionRect;
        public Rect SelectionRect
        {
            get => selectionRect;
            set => Set(ref selectionRect, value);
        }

        public bool IsRangeSelecting => SelectionVisibility == Visibility.Visible;
        public bool IsPreventMutualExclusionSelecting { get; set; }

        #endregion

        public IEnumerable<ISelectableObject> SelectObjects => Fumen.GetAllDisplayableObjects().OfType<ISelectableObject>().Where(x => x.IsSelected).Distinct();

        private Point? currentCursorPosition;
        public Point? CurrentCursorPosition
        {
            get => currentCursorPosition;
            set => Set(ref currentCursorPosition, value);
        }

        public Toast Toast => (GetView() as FumenVisualEditorView)?.mainToast;

        private HashSet<ISelectableObject> currentCopiedSources = new();
        public IEnumerable<ISelectableObject> CurrentCopiedSources => currentCopiedSources;

        #region provide extra MenuItem by plugins

        public void InitExtraMenuItems()
        {
            var ctxMenu = (GetView() as FumenVisualEditorView).EditorContextMenu;

            var extMenuItems = IoC.Get<IEditorExtraContextMenuBuilder>().BuildMenuItems(IoC.GetAll<IFumenVisualEditorExtraMenuItemHandler>(), this);
            foreach (var extMenuItem in extMenuItems)
                ctxMenu.Items.Add(extMenuItem);
        }

        #endregion

        #region Selection Actions

        public void MenuItemAction_SelectAll()
        {
            IsPreventMutualExclusionSelecting = true;

            Fumen.GetAllDisplayableObjects().OfType<ISelectableObject>().ForEach(x => x.IsSelected = true);

            IsPreventMutualExclusionSelecting = false;
        }

        public void MenuItemAction_ReverseSelect()
        {
            IsPreventMutualExclusionSelecting = true;

            Fumen.GetAllDisplayableObjects().OfType<ISelectableObject>().ForEach(x => x.IsSelected = !x.IsSelected);

            IsPreventMutualExclusionSelecting = false;
        }

        public void MenuItemAction_CopySelectedObjects()
        {
            if (IsLocked)
                return;
            //������ѡ���
            currentCopiedSources.Clear();
            currentCopiedSources.AddRange(SelectObjects);

            if (currentCopiedSources.Count == 0)
                ToastNotify($"��ո����б�");
            else
                ToastNotify($"�ն� {currentCopiedSources.Count} �������Ϊ����Դ {(currentCopiedSources.Count == 1 ? ",����Ϊˢ��ģʽ����������Դ" : string.Empty)}");
        }

        public enum PasteMirrorOption
        {
            XGridZeroMirror,
            SelectedRangeCenterXGridMirror,
            SelectedRangeCenterTGridMirror,
            None
        }

        public void MenuItemAction_PasteCopiesObjects()
            => PasteCopiesObjects(PasteMirrorOption.None);
        public void MenuItemAction_PasteCopiesObjectsAsSelectedRangeCenterXGridMirror()
            => PasteCopiesObjects(PasteMirrorOption.SelectedRangeCenterXGridMirror);
        public void MenuItemAction_PasteCopiesObjectsAsSelectedRangeCenterTGridMirror()
            => PasteCopiesObjects(PasteMirrorOption.SelectedRangeCenterTGridMirror);
        public void MenuItemAction_PasteCopiesObjectsAsXGridZeroMirror()
            => PasteCopiesObjects(PasteMirrorOption.XGridZeroMirror);

        public void PasteCopiesObjects(PasteMirrorOption mirrorOption)
        {
            if (IsLocked)
                return;
            //��ȡ��ѡ�����е����
            TryCancelAllObjectSelecting();
            var newObjects = currentCopiedSources.OfType<OngekiObjectBase>().Select(x => x.CopyNew(Fumen)).FilterNull().ToList();
            var mirrorTGrid = CalculateTGridMirror(newObjects, mirrorOption);
            var mirrorXGrid = CalculateXGridMirror(newObjects, mirrorOption);

            var redo = new System.Action(() => { });
            var undo = new System.Action(() => { });

            var idMap = new Dictionary<int, int>();

            var partOfConnectableObjects = newObjects
                .Select(x => x)
                .OfType<ConnectableObjectBase>()
                .GroupBy(x => x.RecordId);

            foreach (var lane in partOfConnectableObjects.Where(x => !x.OfType<ConnectableStartObject>().Any()).ToArray())
            {
                if (lane.IsOnlyOne(out var headChildObject))
                {
                    //ͬid������ֻ�е����ӽڵ㣬�ǾͲ���������ת���͸���ճ����
                    newObjects.RemoveAll(x => x == headChildObject);
                    Log.LogDebug($"detect only one child in same recordId ,remove it. headChildObject : {headChildObject}");
                    continue;
                }

                var refRecordId = -headChildObject.RecordId;
                var refSourceHeadChildObject = currentCopiedSources.Select(x => x).OfType<ConnectableChildObjectBase>().FirstOrDefault(x => x.RecordId == refRecordId);

                var newStartObject = LambdaActivator.CreateInstance(refSourceHeadChildObject.ReferenceStartObject.GetType()) as OngekiObjectBase;

                newStartObject.Copy(headChildObject, Fumen);

                newObjects.RemoveAll(x => x == headChildObject);
                newObjects.Insert(0, newStartObject);

                Log.LogDebug($"detect non-include start object copying , remove head of children and add new start object, headChildObject : {headChildObject}");
            }

            foreach (var displayObjectView in newObjects)
            {
                if (displayObjectView is ITimelineObject timelineObject)
                {
                    var tGrid = timelineObject.TGrid.CopyNew();
                    undo += () => timelineObject.TGrid = tGrid.CopyNew();

                    if (mirrorTGrid is not null)
                    {
                        var offset = mirrorTGrid - tGrid;
                        var newTGrid = mirrorTGrid + offset;

                        redo += () => timelineObject.TGrid = newTGrid.CopyNew();
                    }
                    else
                        redo += () => timelineObject.TGrid = tGrid.CopyNew();
                }

                if (displayObjectView is IHorizonPositionObject horizonPositionObject)
                {
                    var xGrid = horizonPositionObject.XGrid.CopyNew();
                    undo += () => horizonPositionObject.XGrid = xGrid.CopyNew();

                    if (mirrorXGrid is not null)
                    {
                        var offset = mirrorXGrid - xGrid;
                        var newXGrid = mirrorXGrid + offset;

                        redo += () => horizonPositionObject.XGrid = newXGrid.CopyNew();
                    }
                    else
                        redo += () => horizonPositionObject.XGrid = xGrid.CopyNew();
                }

                var selectObj = displayObjectView as ISelectableObject;
                var isSelect = selectObj.IsSelected;

                switch (displayObjectView)
                {
                    case ConnectableStartObject startObject:
                        var rawId = startObject.RecordId;
                        redo += () =>
                        {
                            AddObject(displayObjectView);
                            var newId = startObject.RecordId;
                            idMap[rawId] = newId;
                            selectObj.IsSelected = true;
                        };

                        undo += () =>
                        {
                            RemoveObject(displayObjectView);
                            startObject.RecordId = rawId;
                            selectObj.IsSelected = isSelect;
                        };
                        break;
                    case ConnectableChildObjectBase childObject:
                        var rawChildId = childObject.RecordId;
                        redo += () =>
                        {
                            if (idMap.TryGetValue(rawChildId, out var newChildId))
                                childObject.RecordId = newChildId;
                            AddObject(displayObjectView);
                            selectObj.IsSelected = true;
                        };

                        undo += () =>
                        {
                            RemoveObject(displayObjectView);
                            childObject.RecordId = rawChildId;
                            selectObj.IsSelected = isSelect;
                        };
                        break;
                    default:
                        redo += () =>
                        {
                            AddObject(displayObjectView);
                            selectObj.IsSelected = true;
                        };

                        undo += () =>
                        {
                            RemoveObject(displayObjectView);
                            selectObj.IsSelected = isSelect;
                        };
                        break;
                }
            };

            redo += () =>
            {
                Redraw(RedrawTarget.OngekiObjects | RedrawTarget.TGridUnitLines);
                ToastNotify($"��ճ������ {newObjects.Count} �����");
            };

            undo += () =>
            {
                Redraw(RedrawTarget.OngekiObjects | RedrawTarget.TGridUnitLines);
                ToastNotify($"�ѳ���ճ������");
            };

            UndoRedoManager.ExecuteAction(LambdaUndoAction.Create("����ճ��", redo, undo));
        }

        private XGrid CalculateXGridMirror(IEnumerable<OngekiObjectBase> newObjects, PasteMirrorOption mirrorOption)
        {
            if (mirrorOption == PasteMirrorOption.XGridZeroMirror)
                return XGrid.Zero;

            if (mirrorOption == PasteMirrorOption.SelectedRangeCenterXGridMirror)
            {
                (var min, var max) = newObjects
                .Select(x => x as IHorizonPositionObject)
                .FilterNull()
                .MaxMinBy(x => x.XGrid, (a, b) =>
                {
                    if (a > b)
                        return 1;
                    if (a < b)
                        return -1;
                    return 0;
                });

                var diff = max - min;
                var mirror = min + new GridOffset(0, diff.TotalGrid(min.ResX) / 2);
                return mirror;
            }

            return default;
        }

        private TGrid CalculateTGridMirror(IEnumerable<OngekiObjectBase> newObjects, PasteMirrorOption mirrorOption)
        {
            if (mirrorOption != PasteMirrorOption.SelectedRangeCenterTGridMirror)
                return default;

            (var min, var max) = newObjects
                .Select(x => x as ITimelineObject)
                .FilterNull()
                .MaxMinBy(x => x.TGrid, (a, b) =>
                {
                    if (a > b)
                        return 1;
                    if (a < b)
                        return -1;
                    return 0;
                });

            var diff = max - min;
            var mirror = min + new GridOffset(0, diff.TotalGrid(min.ResT) / 2);
            return mirror;
        }

        private Dictionary<ITimelineObject, double> cacheObjectAudioTime = new();
        private OngekiObjectBase mouseDownHitObject;
        private Point? mouseDownHitObjectPosition;
        private Point mouseStartPosition;
        /// <summary>
        /// ��ʾָ���Ƿ���϶���������Χ
        /// </summary>
        private bool dragOutBound;
        private int currentDraggingActionId;

        public void MenuItemAction_RememberSelectedObjectAudioTime()
        {
            cacheObjectAudioTime.Clear();
            foreach (var obj in SelectObjects)
            {
                if (obj is ITimelineObject timelineObject)
                    cacheObjectAudioTime[timelineObject] = TGridCalculator.ConvertTGridToY(timelineObject.TGrid, this);
                else
                    ToastNotify($"�޷�������������Ϊ�����û��ʵ��ITimelineObject : {obj}");
            }

            ToastNotify($"�Ѽ��� {cacheObjectAudioTime.Count} ���������Ƶʱ��");
        }

        public void MenuItemAction_RecoverySelectedObjectToAudioTime()
        {
            var recoverTargets = Fumen.GetAllDisplayableObjects()
                .OfType<ITimelineObject>()
                .Select(x => cacheObjectAudioTime.TryGetValue(x, out var audioTime) ? (x, audioTime) : default)
                .Where(x => x.x is not null)
                .OrderBy(x => x.audioTime)
                .ToList();

            var undoTargets = recoverTargets.Select(x => x.x).Select(x => (x, x.TGrid)).ToList();

            UndoRedoManager.ExecuteAction(LambdaUndoAction.Create("�ָ��������Ƶʱ��",
                () =>
                {
                    Log.LogInfo($"��ʼ�ָ����ʱ��...");
                    foreach ((var timelineObject, var audioTime) in recoverTargets)
                        timelineObject.TGrid = TGridCalculator.ConvertYToTGrid(audioTime, this);

                    ToastNotify($"�ѻָ� {recoverTargets.Count} ���������Ƶʱ��...");
                }, () =>
                {
                    foreach ((var timelineObject, var undoTGrid) in undoTargets)
                        timelineObject.TGrid = undoTGrid;
                    ToastNotify($"�ѳ��ػָ� {recoverTargets.Count} ���������Ƶʱ��...");
                }
            ));
        }

        #endregion

        private void SelectRangeObjects(Rect selectionRect)
        {
            //todo
            /*
            var selectObjects = EditorViewModels
                .OfType<DisplayObjectViewModelBase>()
                .Where(x => selectionRect.Contains(x.ReferenceOngekiObject is not IHorizonPositionObject ? selectionRect.X : x.CanvasX, x.CanvasY + Setting.JudgeLineOffsetY)).ToArray();

            if (selectObjects.Length == 1)
                NotifyObjectClicked(selectObjects.FirstOrDefault());
            else
                foreach (var o in selectObjects)
                    o.IsSelected = true;
            */
        }

        public void OnFocusableChanged(ActionExecutionContext e)
        {
            Log.LogInfo($"OnFocusableChanged {e.EventArgs}");
        }

        public void OnTimeSignatureListChanged()
        {
            Redraw(RedrawTarget.TGridUnitLines);
        }

        #region Keyboard Actions

        public void KeyboardAction_DeleteSelectingObjects()
        {
            if (IsLocked)
                return;

            //ɾ����ѡ������
            var selectedObject = SelectObjects.OfType<OngekiObjectBase>().ToArray();

            UndoRedoManager.ExecuteAction(LambdaUndoAction.Create("ɾ�����", () =>
            {
                foreach (var obj in selectedObject)
                    RemoveObject(obj);

                Redraw(RedrawTarget.OngekiObjects);
            }, () =>
            {
                foreach (var obj in selectedObject)
                {
                    AddObject(obj);
                }

                Redraw(RedrawTarget.OngekiObjects);
            }));
        }

        public void RemoveObject(OngekiObjectBase obj)
        {
            if (obj is ISelectableObject selectable)
                selectable.IsSelected = false;
            Fumen.RemoveObject(obj);

            var propertyBrowser = IoC.Get<IFumenObjectPropertyBrowser>();
            if (propertyBrowser != null && propertyBrowser.OngekiObject == obj)
                propertyBrowser.SetCurrentOngekiObject(default, this);
        }

        public void KeyboardAction_SelectAllObjects()
        {
            if (IsLocked)
                return;

            Fumen.GetAllDisplayableObjects().OfType<ISelectableObject>().ForEach(x => x.IsSelected = true);
        }

        public void KeyboardAction_CancelSelectingObjects()
        {
            if (IsLocked)
                return;

            //ȡ��ѡ��
            SelectObjects.ForEach(x => x.IsSelected = false);
        }

        public void KeyboardAction_PlayOrPause()
        {
            IoC.Get<IAudioPlayerToolViewer>().RequestPlayOrPause();
        }

        public void KeyboardAction_HideOrShow()
        {
            IsUserRequestHideEditorObject = !IsUserRequestHideEditorObject;
        }
        #endregion

        #region Drag Actions

        public void OnMouseLeave(ActionExecutionContext e)
        {
            IoC.Get<CommonStatusBar>().SubRightMainContentViewModel.Message = string.Empty;
            OnMouseUp(e);
            /*
            if (IsLocked)
                return;

            //Log.LogInfo("OnMouseLeave");
            if (!(IsMouseDown && (e.View as FrameworkElement)?.Parent is IInputElement parent))
                return;
            IsMouseDown = false;
            IsDragging = false;
            var pos = (e.EventArgs as MouseEventArgs).GetPosition(parent);
            SelectObjects.ForEach(x => x.OnDragEnd(pos));
            //e.Handled = true;*/
        }

        public void OnMouseUp(ActionExecutionContext e)
        {
            if (IsLocked)
                return;

            if (!(isMouseDown && (e.View as FrameworkElement)?.Parent is IInputElement parent))
                return;

            if (IsRangeSelecting && SelectionCurrentCursorPosition != SelectionStartPosition)
            {
                SelectRangeObjects(SelectionRect);
            }
            else
            {
                var pos = (e.EventArgs as MouseEventArgs).GetPosition(parent);
                if (isDragging)
                {
                    var cp = pos;
                    cp.Y = ViewHeight - cp.Y + Rect.MinY;
                    SelectObjects.ToArray().ForEach(x => OnObjectDragEnd(x as OngekiObjectBase, cp));
                }
                else
                {
                    //Log.LogDebug($"mouseDownHitObject = {mouseDownHitObject?.ReferenceOngekiObject}");
                    //if no object clicked or alt is pressing , just to process as brush actions.
                    if (mouseDownHitObject is null || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                    {
                        //for object brush
                        if (BrushMode)
                        {
                            TryApplyBrushObject(pos);
                        }
                    }
                    else
                    {
                        if (mouseDownHitObjectPosition is Point p)
                            NotifyObjectClicked(mouseDownHitObject);
                    }
                }
            }

            isMouseDown = false;
            isDragging = false;
            SelectionVisibility = Visibility.Collapsed;
            currentDraggingActionId = int.MaxValue;
        }

        private void TryApplyBrushObject(Point p)
        {
            if (!(CurrentCopiedSources.IsOnlyOne(out var c) && c is OngekiObjectBase copySouceObj))
                return;

            var newObjViewModel = copySouceObj.CopyNew(Fumen);
            if (newObjViewModel is null
                //��֧�ֱ�ˢģʽ���½���������
                || newObjViewModel is ConnectableStartObject
                || newObjViewModel is ConnectableEndObject)
            {
                ToastNotify($"��ˢģʽ�²�֧��{copySouceObj?.Name}");
                return;
            }

            p.Y = ViewHeight - p.Y + Rect.MinY;
            var v = new Vector2((float)p.X, (float)p.Y);

            System.Action undo = () =>
            {
                if (newObjViewModel is ConnectableChildObjectBase childObject)
                {
                    (copySouceObj as ConnectableChildObjectBase)?.ReferenceStartObject.RemoveChildObject(childObject);
                }
                else
                {
                    RemoveObject(newObjViewModel);
                }
                Redraw(RedrawTarget.OngekiObjects);
            };

            System.Action redo = async () =>
            {
                OnObjectMovingCanvas(newObjViewModel, p);
                var x = newObjViewModel is IHorizonPositionObject horizonPositionObject ? XGridCalculator.ConvertXGridToX(horizonPositionObject.XGrid, this) : 0;
                var y = newObjViewModel is ITimelineObject timelineObject ? TGridCalculator.ConvertTGridToY(timelineObject.TGrid, this) : 0;
                var dist = Vector2.Distance(v, new Vector2((float)x, (float)y));
                if (dist > 20)
                {
                    Log.LogDebug($"dist : {dist:F2} > 20 , undo&&discard");
                    undo();

                    Mouse.OverrideCursor = Cursors.No;
                    await Task.Delay(100);
                    Mouse.OverrideCursor = Cursors.Arrow;
                }
                else
                {
                    if (newObjViewModel is ConnectableChildObjectBase childObject)
                    {
                        //todo there is a bug.
                        (copySouceObj as ConnectableChildObjectBase)?.ReferenceStartObject.AddChildObject(childObject);
                    }
                    else
                    {
                        AddObject(newObjViewModel);
                    }
                    Redraw(RedrawTarget.OngekiObjects);
                }
            };

            UndoRedoManager.ExecuteAction(LambdaUndoAction.Create("ˢ��������", redo, undo));
        }

        public void OnMouseDown(ActionExecutionContext e)
        {
            if (IsLocked)
                return;

            var view = e.View as FrameworkElement;

            if ((e.EventArgs as MouseEventArgs).LeftButton == MouseButtonState.Pressed)
            {
                isMouseDown = true;
                isDragging = false;

                var arg = e.EventArgs as MouseEventArgs;
                var hitPoint = arg.GetPosition(e.Source);
                hitPoint.Y = e.Source.ActualHeight - hitPoint.Y + Rect.MinY;
                var hitResult = Enumerable.Empty<KeyValuePair<OngekiObjectBase, Rect>>();
                var position = hitPoint;

                hitResult = hits.AsParallel().Where(x => x.Value.Contains(hitPoint)).ToArray();
                var hitOngekiObjectViewModel = hitResult.FirstOrDefault().Key;

                Log.LogDebug($"mousePos = ��{position.X:F0},{position.Y:F0}) , hitOngekiObjectViewModel = {hitOngekiObjectViewModel}");

                mouseDownHitObject = null;
                mouseDownHitObjectPosition = default;
                mouseStartPosition = position;
                dragOutBound = false;

                if (hitOngekiObjectViewModel is null)
                {
                    TryCancelAllObjectSelecting();

                    //enable show selection
                    SelectionStartPosition = position;
                    SelectionCurrentCursorPosition = position;
                    SelectionVisibility = Visibility.Visible;
                }
                else
                {
                    SelectionVisibility = Visibility.Collapsed;
                    mouseDownHitObject = hitOngekiObjectViewModel;
                    mouseDownHitObjectPosition = position;
                }
            }

            (e.View as FrameworkElement)?.Focus();
        }

        Dictionary<OngekiObjectBase, Point> dragStartCanvasPointMap = new();
        Dictionary<OngekiObjectBase, Point> dragStartPointMap = new();

        public void OnObjectDragEnd(OngekiObjectBase obj, Point pos)
        {
            var dragStartCanvasPoint = dragStartCanvasPointMap[obj];
            var x = obj is IHorizonPositionObject horizonPositionObject ? XGridCalculator.ConvertXGridToX(horizonPositionObject.XGrid, this) : 0;
            var y = obj is ITimelineObject timelineObject ? TGridCalculator.ConvertTGridToY(timelineObject.TGrid, this) : 0;

            OnObjectDragMoving(obj, pos);
            var oldPos = dragStartCanvasPoint;
            var newPos = new Point(x, y);
            UndoRedoManager.ExecuteAction(LambdaUndoAction.Create("����϶�",
                () =>
                {
                    OnObjectMovingCanvas(obj, newPos);
                }, () =>
                {
                    OnObjectMovingCanvas(obj, oldPos);
                }));

            //Log.LogDebug($"OnObjectDragEnd: ({pos.X:F2},{pos.Y:F2}) -> ({x:F2},{y:F2})");

            dragStartCanvasPointMap.Remove(obj);
            dragStartPointMap.Remove(obj);
        }

        public void OnObjectDragMoving(OngekiObjectBase obj, Point pos)
        {
            var dragStartCanvasPoint = dragStartCanvasPointMap[obj];
            var dragStartPoint = dragStartPointMap[obj];

            var movePoint = new Point(
                dragStartCanvasPoint.X + (pos.X - dragStartPoint.X),
                dragStartCanvasPoint.Y + (pos.Y - dragStartPoint.Y)
                );

            //��������һ��
            movePoint.X = Math.Max(0, Math.Min(TotalDurationHeight, movePoint.X));
            movePoint.Y = Math.Max(0, Math.Min(TotalDurationHeight, movePoint.Y));

            //Log.LogDebug($"OnObjectDragMoving: ({pos.X:F2},{pos.Y:F2}) -> ({movePoint.X:F2},{movePoint.Y:F2})");

            OnObjectMovingCanvas(obj, movePoint);
        }

        public void OnObjectMovingCanvas(OngekiObjectBase obj, Point relativePoint)
        {
            if (obj is ITimelineObject timeObj)
            {
                var ry = CheckAndAdjustY(relativePoint.Y);
                if (ry is double dry && TGridCalculator.ConvertYToTGrid(dry, this) is TGrid tGrid)
                {
                    timeObj.TGrid = tGrid;
                    //Log.LogInfo($"Y: {ry} , TGrid: {timeObj.TGrid}");
                }
            }

            if (obj is IHorizonPositionObject posObj)
            {
                var rx = CheckAndAdjustX(relativePoint.X);
                if (rx is double drx)
                {
                    var xGrid = XGridCalculator.ConvertXToXGrid(drx, this);
                    posObj.XGrid = xGrid;
                }

                //Log.LogDebug($"x : {rx:F4} , posObj.XGrid.Unit : {posObj.XGrid.Unit} , xConvertBack : {XGridCalculator.ConvertXGridToX(posObj.XGrid, this)}");
            }
        }

        private class TempCloseLine
        {
            public double distance { get; set; }
            public double value { get; set; }
        }

        public virtual double? CheckAndAdjustY(double y)
        {
            var enableMagneticAdjust = !(Setting.DisableTGridMagneticDock);
            if (!enableMagneticAdjust)
                return y;

            var forceMagneticAdjust = Setting.ForceMagneticDock;
            var fin = forceMagneticAdjust ? TGridCalculator.TryPickClosestBeatTime((float)y, this, 240) : TGridCalculator.TryPickMagneticBeatTime((float)y, 4, this, 240);
            var ry = fin.y;
            if (fin.tGrid == null)
                ry = y;
            //Log.LogDebug($"before y={y:F2} ,select:({fin.tGrid}) ,fin:{ry:F2}");
            return ry;
        }

        public virtual double? CheckAndAdjustX(double x)
        {
            //todo ���ڶ��ַ���ѯ���
            var enableMagneticAdjust = !(Setting.DisableXGridMagneticDock);
            var forceMagneticAdjust = (Setting.ForceMagneticDock) || (Setting.ForceXGridMagneticDock);
            var dockableTriggerDistance = forceMagneticAdjust ? int.MaxValue : 4;
            using var d1 = ObjectPool<List<TempCloseLine>>.GetWithUsingDisposable(out var mid, out var _);
            mid.Clear();
            mid.AddRange(enableMagneticAdjust ? XGridUnitLineLocations?.Select(z =>
            {
                var r = ObjectPool<TempCloseLine>.Get();
                r.distance = Math.Abs(z.X - x);
                r.value = z.X;
                return r;
            })?.Where(z => z.distance < dockableTriggerDistance)?.OrderBy(x => x.distance)?.ToList() : Enumerable.Empty<TempCloseLine>());
            var nearestUnitLine = mid?.FirstOrDefault();
            double? fin = nearestUnitLine != null ? nearestUnitLine.value : (forceMagneticAdjust ? null : x);
            //Log.LogInfo($"nearestUnitLine x:{x:F2} distance:{nearestUnitLine?.distance:F2} fin:{fin}");
            mid.ForEach(x => ObjectPool<TempCloseLine>.Return(x));
            mid.Clear();
            return fin;
        }

        public void OnObjectDragStart(OngekiObjectBase obj, Point pos)
        {
            var x = obj is IHorizonPositionObject horizonPositionObject ? XGridCalculator.ConvertXGridToX(horizonPositionObject.XGrid, this) : 0;
            var y = obj is ITimelineObject timelineObject ? TGridCalculator.ConvertTGridToY(timelineObject.TGrid, this) : 0;

            if (double.IsNaN(x))
                x = default;
            if (double.IsNaN(y))
                y = default;

            dragStartCanvasPointMap[obj] = new Point(x, y);
            dragStartPointMap[obj] = pos;

            //Log.LogDebug($"OnObjectDragStart: ({pos.X:F2},{pos.Y:F2}) -> ({x:F2},{y:F2})");
        }

        public void OnMouseMove(ActionExecutionContext e)
        {
            if ((e.View as FrameworkElement)?.Parent is not IInputElement parent)
                return;
            currentDraggingActionId = int.MaxValue;
            OnMouseMove((e.EventArgs as MouseEventArgs).GetPosition(parent));
        }
        public async void OnMouseMove(Point pos)
        {
            //show current cursor position in statusbar
            UpdateCurrentCursorPosition(pos);

            if (IsLocked)
                return;

            if (!isMouseDown)
                return;

            var r = isDragging;
            isDragging = true;
            var dragCall = new Action<OngekiObjectBase, Point>((vm, pos) =>
            {
                if (r)
                    OnObjectDragMoving(vm, pos);
                else
                    OnObjectDragStart(vm, pos);
            });

            var rp = 1 - pos.Y / ViewHeight;
            var srp = 1 - mouseStartPosition.Y / ViewHeight;
            var offsetY = 0d;

            //const double dragDist = 0.7;
            const double trigPrecent = 0.15;
            const double autoScrollSpeed = 7;

            var offsetYAcc = 0d;
            if (rp >= (1 - trigPrecent) && dragOutBound)
                offsetYAcc = (rp - (1 - trigPrecent)) / trigPrecent;
            else if (rp <= trigPrecent && dragOutBound)
                offsetYAcc = rp / trigPrecent - 1;
            else if (rp < 1 - trigPrecent && rp > trigPrecent)
                dragOutBound = true; //��ָ���ڻ�����Χ���棬��ô�Ϳ��Խ����κεĻ��������ˣ�����ָ��ӻ�����Χ�ڿ�ʼ�͹���
            offsetY = offsetYAcc * autoScrollSpeed;

            var prev = CurrentPlayTime;
            var y = Rect.MinY + Setting.JudgeLineOffsetY + offsetY;

            //Log.LogDebug($"pos={pos.X:F2},{pos.Y:F2} offsetYAcc={offsetYAcc:F2} dragOutBound={dragOutBound} y={y:F2}");

            if (offsetY != 0)
                ScrollTo(y);

            //����жϣ�ȷ�����϶���ѡ��Ʒλ�ã�����˵����ѡ������
            if (IsRangeSelecting)
            {
                //����
                var p = pos;
                p.Y += offsetY;
                p.Y -= 2 * offsetY;
                SelectionCurrentCursorPosition = p;

                var p2 = SelectionStartPosition;
                p2.Y += prev - CurrentPlayTime;
                SelectionStartPosition = p2;
                //Log.LogDebug($"prevY:{-AnimatedScrollViewer.CurrentVerticalOffset + prev:F2} offsetY:{offsetY:F2}");
            }
            else
            {
                //�϶���ѡ���
                var cp = pos;
                cp.Y = ViewHeight - cp.Y + Rect.MinY;
                //Log.LogDebug($"SelectObjects: {SelectObjects.Count()}");
                SelectObjects.ForEach(x => dragCall(x as OngekiObjectBase, cp));
            }

            //�����Ե�
            if (offsetY != 0)
            {
                var currentid = currentDraggingActionId = MathUtils.Random(int.MaxValue - 1);
                await Task.Delay(1000 / 60);
                if (currentDraggingActionId == currentid)
                    OnMouseMove(pos);
            }
        }

        #region Object Click&Selection

        public void TryCancelAllObjectSelecting(params ISelectableObject[] expects)
        {
            var objBrowser = IoC.Get<IFumenObjectPropertyBrowser>();
            var curBrowserObj = objBrowser.OngekiObject;
            expects = expects ?? new ISelectableObject[0];

            if (!(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || IsRangeSelecting || IsPreventMutualExclusionSelecting))
            {
                foreach (var o in SelectObjects.Where(x => !expects.Contains(x)))
                {
                    o.IsSelected = false;
                    if (o == curBrowserObj)
                        objBrowser.SetCurrentOngekiObject(null, this);
                }
            }
        }

        public void NotifyObjectClicked(OngekiObjectBase obj)
        {
            if (obj is not ISelectableObject selectable)
                return;

            var objBrowser = IoC.Get<IFumenObjectPropertyBrowser>();
            var curBrowserObj = objBrowser.OngekiObject;
            var count = SelectObjects.Take(2).Count();
            var first = SelectObjects.FirstOrDefault();

            if ((count > 1) || (count == 1 && first != obj)) //������Ŀǰ�ж����ѡ��ģ����㵥����һ��
            {
                TryCancelAllObjectSelecting(obj as ISelectableObject);
                selectable.IsSelected = true;
                objBrowser.SetCurrentOngekiObject(obj, this);
            }
            else
            {
                selectable.IsSelected = !selectable.IsSelected;
                if (selectable.IsSelected)
                    objBrowser.SetCurrentOngekiObject(obj, this);
                else if (obj == curBrowserObj)
                    objBrowser.SetCurrentOngekiObject(null, this);
                TryCancelAllObjectSelecting(obj as ISelectableObject);
            }
        }

        #endregion

        private void RecalculateSelectionRect()
        {
            var x = Math.Min(SelectionStartPosition.X, SelectionCurrentCursorPosition.X);
            var y = Rect.MaxY - Math.Min(SelectionStartPosition.Y, SelectionCurrentCursorPosition.Y);

            var width = Math.Abs(SelectionStartPosition.X - SelectionCurrentCursorPosition.X);
            var height = Math.Abs(SelectionStartPosition.Y - SelectionCurrentCursorPosition.Y);

            y = y - height + Setting.JudgeLineOffsetY;

            SelectionRect = new Rect(x, y, width, height);
            //Log.LogDebug($"SelectionRect = {SelectionRect}");
        }

        private void UpdateCurrentCursorPosition(ActionExecutionContext e)
        {
            var contentViewModel = IoC.Get<CommonStatusBar>().SubRightMainContentViewModel;
            if ((e.View as FrameworkElement)?.Parent is not IInputElement parent)
            {
                contentViewModel.Message = string.Empty;
                CurrentCursorPosition = null;
                return;
            }
        }

        private void UpdateCurrentCursorPosition(Point pos)
        {
            var contentViewModel = IoC.Get<CommonStatusBar>().SubRightMainContentViewModel;

            var canvasY = Rect.MaxY - pos.Y;
            var canvasX = pos.X;

            var tGrid = TGridCalculator.ConvertYToTGrid(canvasY, this);
            TimeSpan? audioTime = tGrid is not null ? TGridCalculator.ConvertTGridToAudioTime(tGrid, this) : null;
            var xGrid = XGridCalculator.ConvertXToXGrid(canvasX, this);
            contentViewModel.Message = $"C[{canvasX:F2},{canvasY:F2}] {(tGrid is not null ? $"T[{tGrid.Unit},{tGrid.Grid}]" : "T[N/A]")} X[{xGrid.Unit:F2},{xGrid.Grid}] A[{audioTime?.ToString("mm\\:ss\\.fff")}]";
            CurrentCursorPosition = new(canvasX, canvasY);
        }

        public void Grid_DragEnter(ActionExecutionContext e)
        {
            if (IsLocked)
            {
                Log.LogWarn($"discard user actions because editor was locked.");
                return;
            }

            var arg = e.EventArgs as DragEventArgs;
            if (!arg.Data.GetDataPresent(ToolboxDragDrop.DataFormat))
                arg.Effects = DragDropEffects.None;
        }

        public void Grid_Drop(ActionExecutionContext e)
        {
            if (IsLocked)
            {
                Log.LogWarn($"discard user actions because editor was locked.");
                return;
            }

            var arg = e.EventArgs as DragEventArgs;
            if (!arg.Data.GetDataPresent(ToolboxDragDrop.DataFormat))
                return;

            var mousePosition = arg.GetPosition(e.View as FrameworkElement);
            mousePosition.Y = ViewHeight - mousePosition.Y + Rect.MinY;

            switch (arg.Data.GetData(ToolboxDragDrop.DataFormat))
            {
                case ToolboxItem toolboxItem:
                    new DefaultToolBoxDropAction(toolboxItem).Drop(this, mousePosition);
                    break;
                case IEditorDropHandler dropHandler:
                    dropHandler.Drop(this, mousePosition);
                    break;
            }
        }

        #endregion

        private Dictionary<OngekiObjectBase, Rect> hits = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterSelectableObject(OngekiObjectBase obj, System.Numerics.Vector2 centerPos, System.Numerics.Vector2 size)
        {
            //rect.Y = rect.Y - CurrentPlayTime;
            hits[obj] = new Rect(centerPos.X - size.X / 2, centerPos.Y - size.Y / 2, size.X, size.Y);
        }

        public void OnMouseWheel(ActionExecutionContext e)
        {
            if (IsLocked)
                return;

            var arg = e.EventArgs as MouseWheelEventArgs;
            arg.Handled = true;

            ScrollViewerVerticalOffset = ScrollViewerVerticalOffset + Math.Sign(arg.Delta) * 100;
        }

        #region Lock/Unlock User Interaction

        /// <summary>
        /// ��ס�༭�����н����������û��޷��Դ˱༭�����κεĲ���
        /// </summary>
        public void LockAllUserInteraction()
        {
            if (IsLocked)
                return;
            IsLocked = true;
            SelectObjects.ToArray().ForEach(x => x.IsSelected = false);
            ToastNotify($"�༭������ס");
        }

        /// <summary>
        /// �Ӵ��Ա༭���û������ķ���
        /// </summary>
        public void UnlockAllUserInteraction()
        {
            if (!IsLocked)
                return;
            IsLocked = false;
            ToastNotify($"�༭���ѽ���");
        }

        #endregion

        private void ToastNotify(string message)
        {
            Toast.ShowMessage(message);
            Log.LogInfo(message);
        }
    }
}
