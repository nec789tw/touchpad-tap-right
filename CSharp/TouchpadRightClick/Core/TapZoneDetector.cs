using System;
using System.Drawing;

namespace TouchpadRightClick.Core
{
    /// <summary>
    /// Tap Zone 偵測器
    /// 回歸本質 - 只處理右下角右鍵,其他交給 Windows 原生
    /// Magic Numbers 重構 - 提取為命名常數
    /// 設計理念: 不干擾 Windows 原生雙擊/拖曳功能
    /// </summary>
    public class TapZoneDetector
    {
        // D3: 統一為 UI 實際使用的值,消除 Core 預設值與 ModernMainForm 覆寫的分裂
        public const double DEFAULT_TAP_DISTANCE_THRESHOLD = 0.02;  // 2% ~0.6mm (原 0.03)
        public const int DEFAULT_TAP_TIME_MS = 300;                 // Windows 原生標準
        public const double DEFAULT_RIGHT_ZONE_START_X = 0.6;       // 右側區域從 60% 開始 (原 0.5)
        public const double DEFAULT_VERTICAL_SPLIT_Y = 0.5;         // 上下分界

        public const double DEFAULT_EDGE_LEFT_RATIO = 0.04;          // 左邊緣 4%
        public const double DEFAULT_EDGE_RIGHT_RATIO = 0.04;         // 右邊緣 4%
        public const double DEFAULT_EDGE_TOP_RATIO = 0.06;           // 上邊緣 6%

        public const int DEFAULT_DRAG_HOLD_THRESHOLD_MS = 600;       // 600ms 長按進入拖曳
        // 進入拖曳前允許的最大位移（從起點算的高水位）。要比輕觸閾值寬鬆:目標使用者手會抖,
        // 用 2% 會讓拖曳幾乎進不去;但也不能無限,否則從右下角起手慢慢移游標 >600ms 會被當拖曳。
        // 沒實機可調 → 開成 config.json 的進階參數,社群調到對的值再改這裡的預設
        public const double DEFAULT_DRAG_MOVE_TOLERANCE = 0.06;
        private double _dragMoveTolerance = DEFAULT_DRAG_MOVE_TOLERANCE;

        private double _tapDistanceThreshold = DEFAULT_TAP_DISTANCE_THRESHOLD;
        private int _tapTimeThreshold = DEFAULT_TAP_TIME_MS;
        private double _rightZoneStartX = DEFAULT_RIGHT_ZONE_START_X;
        private double _verticalSplitY = DEFAULT_VERTICAL_SPLIT_Y;

        // 邊緣保護
        private double _edgeLeftRatio = DEFAULT_EDGE_LEFT_RATIO;
        private double _edgeRightRatio = DEFAULT_EDGE_RIGHT_RATIO;
        private double _edgeTopRatio = DEFAULT_EDGE_TOP_RATIO;

        private bool _isTracking = false;
        private DateTime _touchStartTime;
        private PointF _touchStartPosition;
        private PointF _lastPosition;
        private double _maxDistance = 0;

        private int _dragHoldThreshold = DEFAULT_DRAG_HOLD_THRESHOLD_MS;
        private bool _isDragging = false;

        public event EventHandler<TapEventArgs> TapDetected;

        public event EventHandler<DragEventArgs> DragStarted;
        public event EventHandler DragEnded;

        /// <summary>
        /// 輕觸距離閾值（觸控板百分比）
        /// </summary>
        public double TapDistanceThreshold
        {
            get => _tapDistanceThreshold;
            set => _tapDistanceThreshold = value;
        }

        /// <summary>長按多久進入拖曳（毫秒）</summary>
        public int DragHoldThresholdMs
        {
            get => _dragHoldThreshold;
            set => _dragHoldThreshold = value;
        }

        /// <summary>進入拖曳前允許的最大位移（0.0-1.0）</summary>
        public double DragMoveTolerance
        {
            get => _dragMoveTolerance;
            set => _dragMoveTolerance = value;
        }

        /// <summary>
        /// 輕觸時間閾值（毫秒）
        /// </summary>
        public int TapTimeThreshold
        {
            get => _tapTimeThreshold;
            set => _tapTimeThreshold = value;
        }

        /// <summary>
        /// 右側區域起始位置（0.0-1.0）
        /// </summary>
        public double RightZoneStartX
        {
            get => _rightZoneStartX;
            set => _rightZoneStartX = value;
        }

        /// <summary>
        /// 垂直分隔線位置（0.0-1.0），決定上下分界
        /// </summary>
        public double VerticalSplitY
        {
            get => _verticalSplitY;
            set => _verticalSplitY = value;
        }


        /// <summary>
        /// 左邊緣保護比例（0.0-1.0）
        /// </summary>
        public double EdgeLeftRatio
        {
            get => _edgeLeftRatio;
            set => _edgeLeftRatio = value;
        }

        /// <summary>
        /// 右邊緣保護比例（0.0-1.0）
        /// </summary>
        public double EdgeRightRatio
        {
            get => _edgeRightRatio;
            set => _edgeRightRatio = value;
        }

        /// <summary>
        /// 上邊緣保護比例（0.0-1.0）
        /// </summary>
        public double EdgeTopRatio
        {
            get => _edgeTopRatio;
            set => _edgeTopRatio = value;
        }

        /// <summary>
        /// 右鍵區域判定（含邊緣保護）。TouchpadMonitor 的預防性左鍵抑制與 DetermineTapZone 共用同一份邏輯,
        /// 否則右邊緣 4% 帶會變成「既不右鍵、左鍵又被抑制」的死區。
        /// </summary>
        public bool IsInRightClickZone(double x, double y)
        {
            var p = new PointF((float)x, (float)y);
            return !IsInEdgeProtectionZone(p) && p.X >= _rightZoneStartX && p.Y >= _verticalSplitY;
        }

        /// <summary>
        /// 強制結束目前的追蹤。Stop()/關閉視窗時呼叫:若正在拖曳會先觸發 DragEnded（讓 TouchpadMonitor 補送 LeftUp）,
        /// 否則系統左鍵會卡在按下狀態,且下次 Start 後第一次抬手會誤送 LeftUp。
        /// </summary>
        public void Reset()
        {
            if (_isDragging)
            {
                _isDragging = false;
                OnDragEnded();
            }
            StopTracking();
        }

        /// <summary>
        /// 處理觸控板事件 - 極簡設計,不干擾 Windows 原生功能
        /// </summary>
        public void ProcessTouchEvent(double x, double y, bool isFirstTouch)
        {
            PointF currentPos = new PointF((float)x, (float)y);

            if (isFirstTouch || !_isTracking)
            {
                // 檢查邊緣保護區域
                if (IsInEdgeProtectionZone(currentPos))
                {
                    return;
                }

                // 不需要 double-tap 判斷,不需要冷卻機制
                // 讓 Windows 處理所有複雜邏輯
                StartTracking(currentPos);
                System.Diagnostics.Debug.WriteLine($"✨ 開始追蹤觸碰");
            }
            else
            {
                // 更新追蹤
                UpdateTracking(currentPos);
            }

            _lastPosition = currentPos;
        }

        /// <summary>
        /// 觸碰結束 - 判斷是否為輕觸
        /// 極簡設計 - 只處理右下角右鍵,其他區域靜默忽略
        /// </summary>
        public void ProcessTouchEnd()
        {
            if (!_isTracking)
            {
                System.Diagnostics.Debug.WriteLine("⚠️  ProcessTouchEnd: 未在追蹤狀態");
                return;
            }

            if (_isDragging)
            {
                System.Diagnostics.Debug.WriteLine($"🔴 結束拖曳模式");
                OnDragEnded();
                _isDragging = false;

                StopTracking();
                return;
            }

            // 檢查是否為輕觸:
            // 1. 總移動距離 < 3% (0.9mm)
            // 2. 總時間 < 300ms (Windows 原生標準)
            TimeSpan duration = DateTime.Now - _touchStartTime;

            System.Diagnostics.Debug.WriteLine($"🔍 ProcessTouchEnd: 距離={_maxDistance:F4} ({_maxDistance*100:F2}%), 時間={duration.TotalMilliseconds:F0}ms");

            if (_maxDistance < _tapDistanceThreshold &&
                duration.TotalMilliseconds < _tapTimeThreshold)
            {
                // 這是輕觸!判斷區域
                TapZone zone = DetermineTapZone(_touchStartPosition);
                System.Diagnostics.Debug.WriteLine($"✅ 輕觸偵測: 區域={zone}, 位置=({_touchStartPosition.X:F2}, {_touchStartPosition.Y:F2})");

                // 其他區域靜默忽略,讓 Windows 原生處理雙擊/拖曳
                if (zone == TapZone.BottomRight)
                {
                    OnTapDetected(new TapEventArgs
                    {
                        Zone = zone,
                        Position = _touchStartPosition,
                        Duration = duration.TotalMilliseconds,
                        MaxDistance = _maxDistance
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"📌 非右下角區域,不發送事件 (交給 Windows 原生處理)");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"❌ 非輕觸: 距離超標={_maxDistance >= _tapDistanceThreshold}, 時間超標={duration.TotalMilliseconds >= _tapTimeThreshold}");
            }

            // 清理狀態
            StopTracking();
        }

        private void StartTracking(PointF position)
        {
            _isTracking = true;
            _touchStartTime = DateTime.Now;
            _touchStartPosition = position;
            _lastPosition = position;
            _maxDistance = 0;
        }

        private void UpdateTracking(PointF currentPos)
        {
            // 計算從起始位置的最大距離
            double distance = CalculateDistance(_touchStartPosition, currentPos);
            if (distance > _maxDistance)
                _maxDistance = distance;

            if (!_isDragging)
            {
                TimeSpan holdDuration = DateTime.Now - _touchStartTime;
                // 進入拖曳的條件是「按住（大致）不動」超過門檻。_maxDistance 是從起點算的高水位:
                // 這次觸碰只要曾經離起點超過 DragMoveTolerance,就永遠不會進拖曳（閂鎖）——
                // 那是從右下角起手的普通游標移動,不能送 LeftDown（否則會框選桌面、拖走檔案）。
                if (holdDuration.TotalMilliseconds > _dragHoldThreshold && _maxDistance < _dragMoveTolerance)
                {
                    // 判斷是否在右下角
                    TapZone currentZone = DetermineTapZone(_touchStartPosition);
                    if (currentZone == TapZone.BottomRight)
                    {
                        // 進入拖曳模式
                        _isDragging = true;

                        System.Diagnostics.Debug.WriteLine($"🟢 進入拖曳模式: 位置=({_touchStartPosition.X:F2}, {_touchStartPosition.Y:F2}), 持續時間={holdDuration.TotalMilliseconds:F0}ms");
                        OnDragStarted(new DragEventArgs { StartPosition = _touchStartPosition });
                    }
                }
            }
        }

        private void StopTracking()
        {
            _isTracking = false;
            _maxDistance = 0;
        }

        private double CalculateDistance(PointF p1, PointF p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private TapZone DetermineTapZone(PointF position)
        {
            if (IsInEdgeProtectionZone(position))
                return TapZone.None;

            if (position.X >= _rightZoneStartX && position.Y >= _verticalSplitY)
            {
                // 右下角區域 = 右鍵
                return TapZone.BottomRight;
            }
            else if (position.X >= _rightZoneStartX && position.Y < _verticalSplitY)
            {
                // 右上角區域 = 左鍵
                return TapZone.TopRight;
            }
            else if (position.X < _rightZoneStartX && position.Y < _verticalSplitY)
            {
                // 左上角區域 = 左鍵
                return TapZone.TopLeft;
            }
            else
            {
                // 左下角區域 = 左鍵
                return TapZone.BottomLeft;
            }
        }

        /// <summary>
        /// 檢查是否在邊緣保護區域
        /// </summary>
        private bool IsInEdgeProtectionZone(PointF position)
        {
            // 左邊緣 4%
            if (position.X < _edgeLeftRatio)
                return true;

            // 右邊緣 4%（從 96% 開始）
            if (position.X > (1.0 - _edgeRightRatio))
                return true;

            // 上邊緣 6%
            if (position.Y < _edgeTopRatio)
                return true;

            return false;
        }

        protected virtual void OnTapDetected(TapEventArgs e)
        {
            TapDetected?.Invoke(this, e);
        }

        protected virtual void OnDragStarted(DragEventArgs e)
        {
            DragStarted?.Invoke(this, e);
        }

        protected virtual void OnDragEnded()
        {
            DragEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Tap Zone 位置
    /// </summary>
    public enum TapZone
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    /// <summary>
    /// Tap 事件參數
    /// </summary>
    public class TapEventArgs : EventArgs
    {
        public TapZone Zone { get; set; }
        public PointF Position { get; set; }
        public double Duration { get; set; }
        public double MaxDistance { get; set; }
    }

    /// <summary>
    /// 拖曳事件參數
    /// </summary>
    public class DragEventArgs : EventArgs
    {
        public PointF StartPosition { get; set; }
    }
}
