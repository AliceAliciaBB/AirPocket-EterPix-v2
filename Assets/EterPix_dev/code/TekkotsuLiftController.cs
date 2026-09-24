using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace AliciliaEterPix.Udon
{
    /// <summary>
    /// 鉄骨シートに着席中のみ、Q/Eキー or VR右スティック縦軸で
    /// 鉄骨の高さ(Controller_TransformChange._value)を直接連続操作する。
    /// SliderSwitch(UIのつまみ、0.1刻み)は経由・同期しない。
    /// 共有FlekSitステーションと同じGameObjectに追加すること。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class TekkotsuLiftController : UdonSharpBehaviour
    {
        [SerializeField]
        Transform _ringCenter;

        [SerializeField]
        float _ringRadius = 1.85f;

        [SerializeField]
        float _ringRadiusTolerance = 0.6f;

        [SerializeField]
        float _ringHeightTolerance = 1.0f;

        [SerializeField]
        Controller_TransformChange _liftController;

        [SerializeField]
        float _moveSpeedPerSecond = 0.5f;

        [SerializeField]
        float _accelerationPerSecond = 2.0f;

        [Tooltip("鉄骨の動きに座席を追従させる(Position/Rotationコンストレイント相当)処理のON/OFF。デバッグ用。")]
        [SerializeField]
        bool _enablePositionFollow = true;

        bool _seatedAtTekkotsu;
        float _vrLookVerticalInput;
        float _currentValue;
        float _currentVelocity;

        Transform _moveTransform;
        Vector3 _localOffsetPosition;
        Quaternion _localOffsetRotation;

        public override void OnStationEntered(VRCPlayerApi player)
        {
            if (!player.isLocal)
            {
                return;
            }
            Vector3 stationPos = transform.position;
            Vector3 centerPos = _ringCenter.position;
            Vector3 stationFlat = new Vector3(stationPos.x, 0f, stationPos.z);
            Vector3 centerFlat = new Vector3(centerPos.x, 0f, centerPos.z);
            float horizontalDistance = Vector3.Distance(stationFlat, centerFlat);
            float heightDifference = Mathf.Abs(stationPos.y - centerPos.y);
            bool onRing = Mathf.Abs(horizontalDistance - _ringRadius) <= _ringRadiusTolerance;
            bool atRingHeight = heightDifference <= _ringHeightTolerance;
            _seatedAtTekkotsu = onRing && atRingHeight;

            if (_seatedAtTekkotsu)
            {
                _currentValue = Mathf.Clamp01(_liftController._value);
                _currentVelocity = 0f;

                // SetParentは着席中のトラッキング復帰を破壊することが確認できたため、
                // 親子関係は変更せず、位置・回転コンストレイント相当を自前で行う。
                _moveTransform = _ringCenter.parent;
                _localOffsetPosition = _moveTransform.InverseTransformPoint(transform.position);
                _localOffsetRotation = Quaternion.Inverse(_moveTransform.rotation) * transform.rotation;
            }
        }

        public override void OnStationExited(VRCPlayerApi player)
        {
            if (!player.isLocal)
            {
                return;
            }
            _seatedAtTekkotsu = false;
            _vrLookVerticalInput = 0f;
            _currentVelocity = 0f;
        }

        public override void InputLookVertical(float value, UdonInputEventArgs args)
        {
            _vrLookVerticalInput = value;
        }

        void Update()
        {
            if (!_seatedAtTekkotsu)
            {
                return;
            }

            float input = 0f;
            if (Input.GetKey(KeyCode.E))
            {
                input += 1f;
            }
            if (Input.GetKey(KeyCode.Q))
            {
                input -= 1f;
            }
            if (Networking.LocalPlayer.IsUserInVR() && Mathf.Abs(_vrLookVerticalInput) > 0.01f)
            {
                input = _vrLookVerticalInput;
            }

            // 軽い慣性: 目標速度に向けてMoveTowardsで加減速する(入力なしなら0へ減速)。
            float targetVelocity = input * _moveSpeedPerSecond;
            _currentVelocity = Mathf.MoveTowards(_currentVelocity, targetVelocity, _accelerationPerSecond * Time.deltaTime);

            if (Mathf.Abs(_currentVelocity) < 0.0001f)
            {
                return;
            }

            float nextValue = Mathf.Clamp01(_currentValue + _currentVelocity * Time.deltaTime);
            // 上下端に当たったら慣性を持ち越さない
            if (nextValue <= 0f || nextValue >= 1f)
            {
                _currentVelocity = 0f;
            }
            _currentValue = nextValue;

            // SliderSwitch(0.1刻み・補間+スナップあり)は経由せず、
            // Controller_TransformChange._value を直接書き換えて鉄骨を動かす。
            // これにより丸めや補間による遅延・スナップの影響を受けず、
            // 生のfloat値でヌルヌル動く。UI側のSliderSwitchとは同期しない。
            _liftController._value = _currentValue;
            _liftController.OnValueChanged();
        }

        void LateUpdate()
        {
            if (!_seatedAtTekkotsu || !_enablePositionFollow)
            {
                return;
            }
            // ムーブの今フレームの位置に対して、着席時に記録したローカルオフセットを
            // 再適用する(位置・回転コンストレイントと同等の処理)。
            transform.position = _moveTransform.TransformPoint(_localOffsetPosition);
            transform.rotation = _moveTransform.rotation * _localOffsetRotation;
        }
    }
}
