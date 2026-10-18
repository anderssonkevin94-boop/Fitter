using Ravenholm.Content;
using UnityEngine;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>Live portrait of the citizen rig with selected equipment attached to its body and hands.</summary>
    public sealed class WorkshopHeroPreview : MonoBehaviour
    {
        private GameObject _stage;
        private HoseRig _rig;
        private Camera _camera;
        private RenderTexture _texture;
        private RawImage _image;
        private ItemDefinition _head, _body, _weapon, _shield;
        private SpriteRenderer _armorLayer, _weaponLayer, _shieldLayer, _portraitHead;
        private Sprite _headSprite;

        // Shown either as the item laid over the body or, on a modular character, worn as its outfit (2026-10-18).
        public bool ArmorShown => _armorLayer != null && _armorLayer.enabled || WornAsOutfit;
        private bool WornAsOutfit => _rig != null && _body != null && _rig.Definition.Modular && _rig.WornOutfit == _rig.Definition.OutfitForArmour(_body.Id);
        public bool WeaponShown => _weaponLayer != null && _weaponLayer.enabled;
        public bool ShieldShown => _shieldLayer != null && _shieldLayer.enabled;
        public bool HeadwearShown => _rig != null && _rig.HeadwearShown;
        public string ShownArmorId => ArmorShown ? _body.Id : null;
        public string ShownWeaponId => WeaponShown ? _weapon.Id : null;
        public string ShownShieldId => ShieldShown ? _shield.Id : null;

        public void SetEquipment(ItemDefinition head, ItemDefinition body, ItemDefinition weapon, ItemDefinition shield)
        {
            _head = head; _body = body; _weapon = weapon;
            _shield = weapon != null && weapon.TwoHanded ? null : shield;
        }

        private void OnEnable()
        {
            var crowd = FindAnyObjectByType<ApplicantCrowdView>();
            if (crowd == null || crowd.Citizen == null) return;
            _stage = new GameObject("EquipmentPortraitStage");
            _stage.transform.position = new Vector3(10000, 10000, 0);
            var figure = CitizenFigure.Create(_stage.transform, "Hero", _stage.transform.position, crowd.Citizen, crowd.Animations);
            var animator = figure.GetComponentInChildren<SupplierAnimator>();
            if (animator != null) animator.enabled = false;
            _rig = figure.GetComponentInChildren<HoseRig>();
            _texture = new RenderTexture(384, 512, 24);
            _texture.Create();
            _image = GetComponent<RawImage>();
            if (_image == null) _image = gameObject.AddComponent<RawImage>();
            _image.texture = _texture; _image.raycastTarget = false;
            var cameraObject = new GameObject("PortraitCamera");
            cameraObject.transform.SetParent(_stage.transform, false);
            cameraObject.transform.localPosition = new Vector3(0, .65f, -10);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.orthographic = true; _camera.orthographicSize = .9f;
            _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = Color.clear;
            _camera.cullingMask = 1 << 31; _camera.targetTexture = _texture;
            _camera.nearClipPlane = .1f; _camera.farClipPlane = 20;
            _camera.allowHDR = false; _camera.allowMSAA = false;
        }

        private void LateUpdate()
        {
            if (_rig == null) return;
            _rig.HeadwearItem = _head != null ? _head.Id : null;
            _rig.ArmourItem = _body != null ? _body.Id : null;   // modular characters wear mapped armour as their outfit
            _rig.Drive("SE", HoseRig.Pose.Idle, 0, Time.unscaledDeltaTime, Vector2.zero, 1);
            DrawArmor();
            DrawHeldItem(ref _weaponLayer, "EquippedWeapon", _weapon, _rig.RightHandAttachment, false);
            DrawHeldItem(ref _shieldLayer, "EquippedShield", _shield, _rig.LeftHandAttachment, true);
            foreach (var t in _stage.GetComponentsInChildren<Transform>()) t.gameObject.layer = 31;
        }

        private SpriteRenderer Layer(ref SpriteRenderer layer, string name, SpriteRenderer anchor, ItemDefinition item)
        {
            if (layer == null && item != null)
            {
                layer = new GameObject(name).AddComponent<SpriteRenderer>();
                layer.transform.SetParent(anchor.transform, false);
                layer.sharedMaterial = anchor.sharedMaterial;
            }
            if (layer == null) return null;
            layer.sprite = item != null ? WorkshopUi.ItemSprite(item) : null;
            layer.enabled = layer.sprite != null;
            return layer.enabled ? layer : null;
        }

        private void DrawArmor()
        {
            // A modular character wearing armour its definition maps to an outfit shows that outfit's torso (2026-10-18):
            // no item picture laid over the body.
            var worn = _rig.Definition.Modular && _body != null && _rig.Definition.OutfitForArmour(_body.Id) != null;
            var layer = Layer(ref _armorLayer, "EquippedArmor", _rig.BodyAttachment, worn ? null : _body);
            _rig.BodyAttachment.enabled = layer == null;
            if (_portraitHead != null) _portraitHead.enabled = layer != null;
            if (layer == null) return;
            if (!_rig.Definition.Modular) DrawPortraitHead();   // a modular head is its own part: it stays as drawn
            var view = _rig.Definition.Get("SE");
            var shoulder = (view.shoulderLeft + view.shoulderRight) * .5f;
            var hips = (view.hipLeft + view.hipRight) * .5f;
            var height = (shoulder.y - hips.y) * 1.95f;
            var size = layer.sprite.bounds.size;
            var scale = height / Mathf.Max(.01f, size.y);
            layer.transform.localScale = Vector3.one * scale;
            layer.transform.localRotation = Quaternion.identity;
            // The collar sits just below the chin; the coat hem overlaps the hips. Uniform scale preserves the art.
            layer.transform.localPosition = new Vector3(shoulder.x, shoulder.y + .12f - height * .5f, -.001f);
            layer.sortingOrder = 5;
        }

        private void DrawPortraitHead()
        {
            var anchor = _rig.BodyAttachment;
            if (_portraitHead == null)
            {
                // Citizen body_SE paints head and shirt together. Show its authored head region above the coat,
                // rather than leaving the blue shirt protruding through the armour. The source texture is unchanged.
                var source = anchor.sprite;
                var neckY = _rig.Definition.Get("SE").shoulderRight.y + .09f;
                var cut = Mathf.Clamp(source.pivot.y + neckY * source.pixelsPerUnit, 0, source.rect.height - 1);
                var rect = new Rect(source.rect.x, source.rect.y + cut, source.rect.width, source.rect.height - cut);
                _headSprite = Sprite.Create(source.texture, rect, new Vector2(.5f, 0), source.pixelsPerUnit);
                // The SE source includes shirt shoulders beside the neck. Clip those two corners with sprite
                // geometry, preserving the original face and neck pixels (coordinates authored on its 512px canvas).
                var width = source.rect.width;
                var fullHeight = source.rect.height;
                var seam = fullHeight * (1f - 211f / 512f) - cut;
                var neck = Mathf.Max(0, fullHeight * (1f - 238f / 512f) - cut);
                // OverrideGeometry accepts pixel coordinates in the sprite rectangle, unlike Sprite.vertices.
                Vector2 Point(float x, float y) => new Vector2(Mathf.Clamp(width * x, .5f, width - .5f),
                    Mathf.Clamp(y, .5f, _headSprite.rect.height - .5f));
                var top = rect.height;
                _headSprite.OverrideGeometry(new[] {
                    Point(0, seam), Point(1, seam), Point(0, top), Point(1, top),
                    Point(222f / 512f, seam), Point(305f / 512f, seam), Point(240f / 512f, neck), Point(295f / 512f, neck)
                }, new ushort[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6 });
                _headSprite.hideFlags = HideFlags.HideAndDontSave;
                _portraitHead = new GameObject("PortraitHead").AddComponent<SpriteRenderer>();
                _portraitHead.transform.SetParent(anchor.transform, false);
                _portraitHead.sharedMaterial = anchor.sharedMaterial;
                _portraitHead.sprite = _headSprite;
                _portraitHead.transform.localPosition = new Vector3((source.rect.width * .5f - source.pivot.x) / source.pixelsPerUnit,
                    (cut - source.pivot.y) / source.pixelsPerUnit, -.002f);
                _portraitHead.sortingOrder = 6;
            }
            _portraitHead.enabled = true;
        }

        private void DrawHeldItem(ref SpriteRenderer layer, string name, ItemDefinition item, SpriteRenderer hand, bool shield)
        {
            var drawing = Layer(ref layer, name, hand, item);
            if (drawing == null) return;
            float height;
            if (shield) height = .46f;
            else if (item.WeaponTag == WeaponTag.LightBlade) height = .34f;
            else if (item.WeaponTag == WeaponTag.Staff) height = .92f;
            else if (item.WeaponTag == WeaponTag.Bow) height = .72f;
            else height = item.TwoHanded ? .78f : .61f;
            var scale = height / Mathf.Max(.01f, drawing.sprite.bounds.size.y);
            drawing.transform.localScale = Vector3.one * scale;
            var angle = shield ? 0 : item.WeaponTag == WeaponTag.Bow ? 8 : 24;
            drawing.transform.localRotation = Quaternion.Euler(0, 0, angle);
            // Upright blades/staves have their grip near the base; bows and shields are held at their centre.
            var gripY = shield || item.WeaponTag == WeaponTag.Bow ? .5f : .18f;
            var offset = Quaternion.Euler(0, 0, angle) * new Vector3(0, (.5f - gripY) * height, 0);
            drawing.transform.localPosition = offset;
            drawing.sortingOrder = 8;
            hand.sprite = _rig.Definition.handGripSide;
            hand.sortingOrder = 9;
        }

        private void OnDisable()
        {
            if (_camera != null) _camera.targetTexture = null;
            if (_image != null) _image.texture = null;
            if (_stage != null) { _stage.SetActive(false); Destroy(_stage); }
            if (_texture != null) { _texture.Release(); Destroy(_texture); }
            _rig = null; _camera = null; _stage = null; _texture = null;
            _armorLayer = null; _weaponLayer = null; _shieldLayer = null; _portraitHead = null;
            if (_headSprite != null) Destroy(_headSprite);
            _headSprite = null;
        }
    }
}
