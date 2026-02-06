import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import HomePage from "./components/homepage/HomePage"
import { ToastContainer } from './components/homepage/content/ToastContainer'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ToastContainer />
    <HomePage />
  </StrictMode>,
)
