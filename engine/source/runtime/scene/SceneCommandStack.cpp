#include "scene/SceneCommandStack.h"

#include <stdexcept>

namespace NcmaEngine
{
    void SceneCommandStack::Push(std::string label, State before, State after)
    {
        if (m_Cursor < m_Commands.size())
        {
            m_Commands.erase(m_Commands.begin() + static_cast<std::ptrdiff_t>(m_Cursor), m_Commands.end());
            if (m_SavedCursor.has_value() && *m_SavedCursor > m_Cursor)
                m_SavedCursor.reset();
        }
        m_Commands.push_back({std::move(label), std::move(before), std::move(after)});
        m_Cursor = m_Commands.size();
    }

    bool SceneCommandStack::Undo(
        SceneWorld& world, std::optional<SceneUuid>& selection, std::string& error)
    {
        if (!CanUndo())
            return false;
        const Command& command = m_Commands[m_Cursor - 1];
        if (!world.RestoreSnapshot(command.Before.Scene, error))
            return false;
        selection = command.Before.Selection;
        --m_Cursor;
        return true;
    }

    bool SceneCommandStack::Redo(
        SceneWorld& world, std::optional<SceneUuid>& selection, std::string& error)
    {
        if (!CanRedo())
            return false;
        const Command& command = m_Commands[m_Cursor];
        if (!world.RestoreSnapshot(command.After.Scene, error))
            return false;
        selection = command.After.Selection;
        ++m_Cursor;
        return true;
    }

    void SceneCommandStack::Clear() noexcept
    {
        m_Commands.clear();
        m_Cursor = 0;
        m_SavedCursor = 0;
    }

    void SceneCommandStack::MarkSaved() noexcept { m_SavedCursor = m_Cursor; }

    bool SceneCommandStack::IsDirty() const noexcept
    {
        return !m_SavedCursor.has_value() || *m_SavedCursor != m_Cursor;
    }

    const std::string& SceneCommandStack::UndoLabel() const
    {
        if (!CanUndo())
            throw std::logic_error("No scene command is available to undo");
        return m_Commands[m_Cursor - 1].Label;
    }

    const std::string& SceneCommandStack::RedoLabel() const
    {
        if (!CanRedo())
            throw std::logic_error("No scene command is available to redo");
        return m_Commands[m_Cursor].Label;
    }
}
