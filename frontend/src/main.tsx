import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import HomePage from "./features/home/layout/HomePage"
import { ToastContainer } from './components/common/ToastContainer'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ToastContainer />
    <HomePage />
  </StrictMode>,
)

