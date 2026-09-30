// Hand-written declarations of the editor members the authoring package uses. Same bargain
// as UnityEngine.cs: empty bodies, exact signatures, only what is called.

namespace UnityEditor
{
    public sealed class MenuItemAttribute : System.Attribute
    {
        public MenuItemAttribute(string itemName) { }
        public MenuItemAttribute(string itemName, bool isValidateFunction) { }
        public MenuItemAttribute(string itemName, bool isValidateFunction, int priority) { }
        public int priority { get; set; }
    }
}
