import { useState, useEffect, useRef } from 'react'
import { ChevronDown } from 'lucide-react'
import './PremiumSelect.css'

/**
 * PremiumSelect Component
 * A customizable and high-end alternative to the native <select> element.
 */
export default function PremiumSelect({ 
  value, 
  options, 
  onChange, 
  placeholder = "Seleccionar...", 
  icon, 
  labelKey = 'label', 
  valueKey = 'value' 
}) {
  const [isOpen, setIsOpen] = useState(false)
  const containerRef = useRef(null)

  useEffect(() => {
    const handleClickOutside = (e) => {
      if (containerRef.current && !containerRef.current.contains(e.target)) {
        setIsOpen(false)
      }
    }
    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [])

  const selectedOption = options.find(opt => opt[valueKey] === value) || options[0]

  return (
    <div className={`premium-select-container ${isOpen ? 'is-open' : ''}`} ref={containerRef}>
      <div className="premium-select-trigger" onClick={() => setIsOpen(!isOpen)}>
        <div className="trigger-content">
          {icon && <span className="trigger-icon">{icon}</span>}
          <span className="trigger-text">
            {selectedOption ? (selectedOption[labelKey] || selectedOption[valueKey]) : placeholder}
          </span>
        </div>
        <ChevronDown size={14} className={`trigger-arrow ${isOpen ? 'rotated' : ''}`} />
      </div>
      
      {isOpen && (
        <div className="premium-select-dropdown">
          {options.map((opt, i) => (
            <div 
              key={i} 
              className={`premium-select-option ${opt[valueKey] === value ? 'selected' : ''}`}
              onClick={() => {
                onChange(opt[valueKey])
                setIsOpen(false)
              }}
            >
              {opt[labelKey] || opt[valueKey]}
            </div>
          ))}
          {options.length === 0 && (
            <div className="premium-select-option empty">No hay opciones disponibles</div>
          )}
        </div>
      )}
    </div>
  )
}
