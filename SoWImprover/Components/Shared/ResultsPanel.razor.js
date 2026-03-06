import { Editor } from 'https://esm.sh/@tiptap/core@2.27.2?target=es2022';
import StarterKit from 'https://esm.sh/@tiptap/starter-kit@2.27.2?target=es2022';
import { Markdown } from 'https://esm.sh/tiptap-markdown@0.8.10?target=es2022';
import Table from 'https://esm.sh/@tiptap/extension-table@2.27.2?target=es2022';
import TableRow from 'https://esm.sh/@tiptap/extension-table-row@2.27.2?target=es2022';
import TableHeader from 'https://esm.sh/@tiptap/extension-table-header@2.27.2?target=es2022';
import TableCell from 'https://esm.sh/@tiptap/extension-table-cell@2.27.2?target=es2022';

const editors = new Map();

// Document-level event delegation for toolbar buttons.
// Survives Blazor DOM re-renders (which replace elements and lose attached listeners).
document.addEventListener('click', (e) => {
    const btn = e.target.closest('[data-cmd]');
    if (!btn) return;
    const toolbar = btn.closest('.app-editor-toolbar');
    if (!toolbar) return;
    const editorId = toolbar.id.replace('toolbar-', 'editor-');
    const editor = editors.get(editorId);
    if (!editor) return;
    e.preventDefault();
    e.stopPropagation();
    const cmd = btn.dataset.cmd;
    const chain = editor.chain().focus();
    switch (cmd) {
        case 'toggleHeading':
            chain.toggleHeading({ level: 3 }).run(); break;
        case 'insertTable':
            chain.insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run(); break;
        default:
            if (typeof chain[cmd] === 'function') chain[cmd]().run();
            break;
    }
});

export function createEditor(elementId, markdown) {
    destroyEditor(elementId);

    const el = document.getElementById(elementId);
    if (!el) return false;

    const editor = new Editor({
        element: el,
        extensions: [
            StarterKit,
            Markdown,
            Table.configure({ resizable: false }),
            TableRow,
            TableHeader,
            TableCell,
        ],
        content: markdown,
    });

    editors.set(elementId, editor);
    return true;
}

export function getMarkdown(elementId) {
    const editor = editors.get(elementId);
    if (!editor) return null;
    return editor.storage.markdown?.getMarkdown() ?? '';
}

export function destroyEditor(elementId) {
    const editor = editors.get(elementId);
    if (editor) {
        editor.destroy();
        editors.delete(elementId);
    }
}
