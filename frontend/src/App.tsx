import { AuthGate } from './features/auth/AuthGate'

function App() {
  return (
    <main className="container">
      <h1>Quests</h1>
      <AuthGate />
    </main>
  )
}

export default App
