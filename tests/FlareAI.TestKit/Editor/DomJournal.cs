using System;
using System.Collections.Generic;
using System.Xml;

namespace FlareAI.TestKit.Editor
{
    /// <summary>
    /// Records every change to the attached DOM (inserts, removals, value changes, attributes included) through the
    /// XmlDocument change events, so an operation can be undone and redone in place: node objects survive, as in Flare,
    /// and Undo restores the original bytes. Changes to detached nodes (fragments being built) are not recorded.
    /// </summary>
    internal sealed class DomJournal : IDisposable
    {
        private readonly XmlDocument _doc;
        private List<DomChange> _sink;
        private int _replaying;

        /// <summary>Called for an attached change; the bool says whether it was recorded into an operation.</summary>
        public Action<string, bool> Observed;

        public DomJournal(XmlDocument doc)
        {
            _doc = doc;
            _doc.NodeInserted += OnInserted;
            _doc.NodeRemoving += OnRemoving;
            _doc.NodeChanging += OnChanging;
        }

        public void Dispose()
        {
            _doc.NodeInserted -= OnInserted;
            _doc.NodeRemoving -= OnRemoving;
            _doc.NodeChanging -= OnChanging;
        }

        /// <summary>Starts recording into <paramref name="sink"/>; null stops.</summary>
        public void RecordInto(List<DomChange> sink) { _sink = sink; }

        public void Undo(IList<DomChange> changes)
        {
            _replaying++;
            try { for (var i = changes.Count - 1; i >= 0; i--) changes[i].Undo(); }
            finally { _replaying--; }
        }

        public void Redo(IList<DomChange> changes)
        {
            _replaying++;
            try { foreach (var c in changes) c.Redo(); }
            finally { _replaying--; }
        }

        private void OnInserted(object sender, XmlNodeChangedEventArgs e)
        {
            if (_replaying > 0 || !IsAttached(e.NewParent)) return;
            var c = e.Node is XmlAttribute
                ? (DomChange)new AttributeInserted((XmlAttribute)e.Node, (XmlElement)e.NewParent)
                : new NodeInserted(e.Node, e.NewParent);
            Note(c, "insert " + Describe(e.Node) + " into " + Describe(e.NewParent));
        }

        private void OnRemoving(object sender, XmlNodeChangedEventArgs e)
        {
            if (_replaying > 0 || !IsAttached(e.OldParent)) return;
            var c = e.Node is XmlAttribute
                ? (DomChange)new AttributeRemoved((XmlAttribute)e.Node, (XmlElement)e.OldParent)
                : new NodeRemoved(e.Node, e.OldParent);
            Note(c, "remove " + Describe(e.Node) + " from " + Describe(e.OldParent));
        }

        private void OnChanging(object sender, XmlNodeChangedEventArgs e)
        {
            if (_replaying > 0 || !IsAttached(e.Node)) return;
            Note(new ValueChanged(e.Node, e.OldValue, e.NewValue), "change " + Describe(e.Node));
        }

        private void Note(DomChange c, string what)
        {
            var recorded = _sink != null;
            if (recorded) _sink.Add(c);
            var o = Observed;
            if (o != null) o(what, recorded);
        }

        /// <summary>True when the node hangs (through parents or an attribute's owner) off the document.</summary>
        public static bool IsAttached(XmlNode n)
        {
            while (n != null)
            {
                if (n.NodeType == XmlNodeType.Document) return true;
                n = n is XmlAttribute ? ((XmlAttribute)n).OwnerElement : n.ParentNode;
            }
            return false;
        }

        private static string Describe(XmlNode n)
        {
            if (n == null) return "(null)";
            switch (n.NodeType)
            {
                case XmlNodeType.Text:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    var v = n.Value ?? "";
                    return "text \"" + (v.Length > 40 ? v.Substring(0, 40) + "…" : v) + "\"";
                case XmlNodeType.Attribute: return "@" + n.Name;
                default: return "<" + n.Name + ">";
            }
        }
    }

    internal abstract class DomChange
    {
        public abstract void Undo();
        public abstract void Redo();
    }

    internal sealed class NodeInserted : DomChange
    {
        private readonly XmlNode _node, _parent, _next;
        public NodeInserted(XmlNode node, XmlNode parent) { _node = node; _parent = parent; _next = node.NextSibling; }
        public override void Undo() { _parent.RemoveChild(_node); }
        public override void Redo() { _parent.InsertBefore(_node, _next); }
    }

    internal sealed class NodeRemoved : DomChange
    {
        private readonly XmlNode _node, _parent, _next;
        public NodeRemoved(XmlNode node, XmlNode parent) { _node = node; _parent = parent; _next = node.NextSibling; }
        public override void Undo() { _parent.InsertBefore(_node, _next); }
        public override void Redo() { _parent.RemoveChild(_node); }
    }

    internal sealed class AttributeInserted : DomChange
    {
        private readonly XmlAttribute _attr;
        private readonly XmlElement _owner;
        private readonly int _index;
        public AttributeInserted(XmlAttribute attr, XmlElement owner) { _attr = attr; _owner = owner; _index = IndexOf(owner, attr); }
        public override void Undo() { _owner.Attributes.Remove(_attr); }
        public override void Redo() { Insert(_owner, _attr, _index); }

        internal static int IndexOf(XmlElement owner, XmlAttribute attr)
        {
            for (var i = 0; i < owner.Attributes.Count; i++) if (owner.Attributes[i] == attr) return i;
            return owner.Attributes.Count;
        }

        internal static void Insert(XmlElement owner, XmlAttribute attr, int index)
        {
            if (index >= 0 && index < owner.Attributes.Count) owner.Attributes.InsertBefore(attr, owner.Attributes[index]);
            else owner.Attributes.Append(attr);
        }
    }

    internal sealed class AttributeRemoved : DomChange
    {
        private readonly XmlAttribute _attr;
        private readonly XmlElement _owner;
        private readonly int _index;
        public AttributeRemoved(XmlAttribute attr, XmlElement owner) { _attr = attr; _owner = owner; _index = AttributeInserted.IndexOf(owner, attr); }
        public override void Undo() { AttributeInserted.Insert(_owner, _attr, _index); }
        public override void Redo() { _owner.Attributes.Remove(_attr); }
    }

    internal sealed class ValueChanged : DomChange
    {
        private readonly XmlNode _node;
        private readonly string _old, _new;
        public ValueChanged(XmlNode node, string oldValue, string newValue) { _node = node; _old = oldValue; _new = newValue; }
        public override void Undo() { _node.Value = _old; }
        public override void Redo() { _node.Value = _new; }
    }
}
