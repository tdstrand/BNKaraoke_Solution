// src/components/SortableItem.tsx
import React from 'react';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { HolderOutlined } from '@ant-design/icons';
import './SortableItem.css';

interface SortableItemProps {
  id: string;
  disabled?: boolean;
  className?: string;
  children: React.ReactNode;
}

export const SortableItem: React.FC<SortableItemProps> = ({ id, disabled = false, className, children }) => {
  const { attributes, listeners, setNodeRef, transform, transition } = useSortable({
    id,
    disabled,
  });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
  };

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...attributes}
      className={`mobile-sortable-item ${className || ''}`}
      data-disabled={disabled || undefined}
    >
      <span className="drag-handle" {...listeners}>
        <HolderOutlined />
      </span>
      {children}
    </div>
  );
};