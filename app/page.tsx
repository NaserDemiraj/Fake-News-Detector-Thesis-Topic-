"use client"

import { useState } from "react"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Textarea } from "@/components/ui/textarea"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Progress } from "@/components/ui/progress"
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip"
import { Badge } from "@/components/ui/badge"
import { Separator } from "@/components/ui/separator"
import {
  CheckCircle,
  AlertTriangle,
  AlertCircle,
  Newspaper,
  LinkIcon,
  FileText,
  Info,
  BarChart3,
  Shield,
  History,
  BookOpen,
  Search,
  Lightbulb,
  ExternalLink,
  ChevronRight,
  Loader2,
} from "lucide-react"

// Point to our local Next.js API which coordinates AI + .NET DB
const API_URL = "/api/analyze"

// Sample recent analyses for the dashboard
const recentAnalyses = [
  {
    title: "Climate Change Report",
    url: "https://example.com/climate-report",
    score: 85,
    date: "2 hours ago",
  },
  {
    title: "Political Scandal Exposed",
    url: "https://example.com/political-scandal",
    score: 32,
    date: "Yesterday",
  },
  {
    title: "Health Benefits of Coffee",
    url: "https://example.com/coffee-benefits",
    score: 76,
    date: "2 days ago",
  },
]

// Sample tips for identifying fake news
const fakeTips = [
  "Check the source's credibility and reputation",
  "Look for unusual URLs or site names",
  "Check for poor spelling and grammar",
  "Look for excessive punctuation or ALL CAPS",
  "Verify if other reputable sources are reporting the same news",
]

export default function FakeNewsDetector() {
  const [url, setUrl] = useState("")
  const [text, setText] = useState("")
  const [isAnalyzing, setIsAnalyzing] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [analysisCount, setAnalysisCount] = useState(0)
  const [showAuthModal, setShowAuthModal] = useState(false)
  const [isLoggedIn, setIsLoggedIn] = useState(false)
  const [activeTab, setActiveTab] = useState<string>("dashboard")
  const [result, setResult] = useState<null | {
    success: boolean
    score: number
    verdict: string
    explanation: string
    factors: { name: string; score: number }[]
  }>(null)

  const analyzeNews = async (type: "url" | "text") => {
    // Check if user needs to login after 3 free analyses
    if (!isLoggedIn && analysisCount >= 3) {
      setShowAuthModal(true)
      return
    }

    setIsAnalyzing(true)
    setError(null)
    setActiveTab("results")

    try {
      // Call the .NET backend directly
      const response = await fetch(API_URL, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          type,
          content: type === "url" ? url : text,
        }),
      })

      if (!response.ok) {
        throw new Error(`Error: ${response.status} ${response.statusText}`)
      }

      const data = await response.json()
      setResult(data)
      setAnalysisCount(prev => prev + 1)
    } catch (error) {
      console.error("Error analyzing news:", error)
      setError(error instanceof Error ? error.message : "Failed to analyze content")
    } finally {
      setIsAnalyzing(false)
    }
  }

  const getStatusIcon = (score: number) => {
    if (score > 70) return <CheckCircle className="h-6 w-6 text-green-500" />
    if (score > 40) return <AlertTriangle className="h-6 w-6 text-yellow-500" />
    return <AlertCircle className="h-6 w-6 text-red-500" />
  }

  const getStatusColor = (score: number) => {
    if (score > 70) return "bg-green-500"
    if (score > 40) return "bg-yellow-500"
    return "bg-red-500"
  }

  const getStatusText = (score: number) => {
    if (score > 70) return "text-green-500"
    if (score > 40) return "text-yellow-500"
    return "text-red-500"
  }

  const getStatusBg = (score: number) => {
    if (score > 70) return "bg-green-50"
    if (score > 40) return "bg-yellow-50"
    return "bg-red-50"
  }

  const getStatusBorder = (score: number) => {
    if (score > 70) return "border-green-200"
    if (score > 40) return "border-yellow-200"
    return "border-red-200"
  }

  return (
    <div className="min-h-screen bg-gradient-to-b from-gray-50 to-gray-100 dark:from-gray-900 dark:to-gray-800">
      <header className="bg-white dark:bg-gray-950 border-b border-gray-200 dark:border-gray-800 sticky top-0 z-10">
        <div className="container mx-auto px-4 py-3 flex items-center justify-between">
          <div className="flex items-center space-x-2">
            <Shield className="h-6 w-6 text-blue-600 dark:text-blue-400" />
            <h1 className="text-xl font-bold text-gray-900 dark:text-white">Fake News Detector</h1>
          </div>
          <div className="flex items-center space-x-4">
            <TooltipProvider>
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button variant="outline" size="sm">
                    <Info className="h-4 w-4 mr-2" />
                    About
                  </Button>
                </TooltipTrigger>
                <TooltipContent>
                  <p className="w-80">
                    This tool uses advanced AI and natural language processing to analyze news content and determine its
                    credibility based on multiple factors.
                  </p>
                </TooltipContent>
              </Tooltip>
            </TooltipProvider>
            <Button variant="ghost" size="sm">
              <History className="h-4 w-4 mr-2" />
              History
            </Button>
          </div>
        </div>
      </header>

      <main className="container mx-auto py-8 px-4">
        <div className="grid grid-cols-1 lg:grid-cols-4 gap-6">
          {/* Sidebar */}
          <div className="lg:col-span-1">
            <Card className="sticky top-24">
              <CardHeader className="pb-3">
                <CardTitle>Navigation</CardTitle>
              </CardHeader>
              <CardContent className="p-0">
                <div className="space-y-1 px-3">
                  <Button
                    variant={activeTab === "dashboard" ? "default" : "ghost"}
                    className="w-full justify-start"
                    onClick={() => setActiveTab("dashboard")}
                  >
                    <BarChart3 className="h-4 w-4 mr-2" />
                    Dashboard
                  </Button>
                  <Button
                    variant={activeTab === "analyze" ? "default" : "ghost"}
                    className="w-full justify-start"
                    onClick={() => setActiveTab("analyze")}
                  >
                    <Search className="h-4 w-4 mr-2" />
                    Analyze Content
                  </Button>
                  {result && (
                    <Button
                      variant={activeTab === "results" ? "default" : "ghost"}
                      className="w-full justify-start"
                      onClick={() => setActiveTab("results")}
                    >
                      <BarChart3 className="h-4 w-4 mr-2" />
                      Results
                    </Button>
                  )}
                </div>
              </CardContent>
              <Separator className="my-3" />
              <CardContent>
                <h3 className="font-medium mb-2 flex items-center">
                  <Lightbulb className="h-4 w-4 mr-2 text-yellow-500" />
                  Fake News Tips
                </h3>
                <ul className="space-y-2 text-sm">
                  {fakeTips.map((tip, index) => (
                    <li key={index} className="flex items-start">
                      <ChevronRight className="h-4 w-4 mr-1 mt-0.5 text-blue-500" />
                      <span>{tip}</span>
                    </li>
                  ))}
                </ul>
              </CardContent>
            </Card>
          </div>

          {/* Main Content */}
          <div className="lg:col-span-3 space-y-6">
            {activeTab === "dashboard" && (
              <div className="space-y-6">
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  <Card>
                    <CardHeader className="pb-2">
                      <CardTitle className="text-sm font-medium text-muted-foreground">Total Analyses</CardTitle>
                    </CardHeader>
                    <CardContent>
                      <div className="text-2xl font-bold">124</div>
                      <p className="text-xs text-muted-foreground mt-1">+12% from last week</p>
                    </CardContent>
                  </Card>
                  <Card>
                    <CardHeader className="pb-2">
                      <CardTitle className="text-sm font-medium text-muted-foreground">Fake News Detected</CardTitle>
                    </CardHeader>
                    <CardContent>
                      <div className="text-2xl font-bold">37</div>
                      <p className="text-xs text-muted-foreground mt-1">29.8% of total analyses</p>
                    </CardContent>
                  </Card>
                  <Card>
                    <CardHeader className="pb-2">
                      <CardTitle className="text-sm font-medium text-muted-foreground">Average Score</CardTitle>
                    </CardHeader>
                    <CardContent>
                      <div className="text-2xl font-bold">62.4%</div>
                      <p className="text-xs text-muted-foreground mt-1">+3.2% from last week</p>
                    </CardContent>
                  </Card>
                </div>

                <Card>
                  <CardHeader>
                    <CardTitle>Recent Analyses</CardTitle>
                    <CardDescription>Your recently analyzed content</CardDescription>
                  </CardHeader>
                  <CardContent>
                    <div className="space-y-4">
                      {recentAnalyses.map((item, index) => (
                        <div
                          key={index}
                          className="flex items-center justify-between p-3 rounded-lg border bg-card hover:bg-accent/50 transition-colors"
                        >
                          <div className="flex items-center space-x-3">
                            <div
                              className={`p-2 rounded-full ${getStatusBg(item.score)} ${getStatusBorder(item.score)}`}
                            >
                              {getStatusIcon(item.score)}
                            </div>
                            <div>
                              <h3 className="font-medium">{item.title}</h3>
                              <div className="flex items-center text-sm text-muted-foreground">
                                <LinkIcon className="h-3 w-3 mr-1" />
                                <span className="truncate max-w-[200px]">{item.url}</span>
                              </div>
                            </div>
                          </div>
                          <div className="flex items-center space-x-4">
                            <div className="text-sm text-muted-foreground">{item.date}</div>
                            <Badge variant={item.score > 70 ? "success" : item.score > 40 ? "warning" : "destructive"}>
                              {Math.round(item.score)}%
                            </Badge>
                            <Button variant="ghost" size="icon">
                              <ExternalLink className="h-4 w-4" />
                            </Button>
                          </div>
                        </div>
                      ))}
                    </div>
                  </CardContent>
                  <CardFooter>
                    <Button variant="outline" className="w-full">
                      View All Analyses
                    </Button>
                  </CardFooter>
                </Card>

                <Card>
                  <CardHeader>
                    <CardTitle>How It Works</CardTitle>
                    <CardDescription>Understanding our fake news detection process</CardDescription>
                  </CardHeader>
                  <CardContent>
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
                      <div className="flex flex-col items-center text-center p-4 rounded-lg border bg-card">
                        <div className="p-3 rounded-full bg-blue-50 border border-blue-100 mb-3">
                          <Search className="h-6 w-6 text-blue-600" />
                        </div>
                        <h3 className="font-medium mb-2">Content Analysis</h3>
                        <p className="text-sm text-muted-foreground">
                          We analyze the text for emotional language, clickbait tactics, and factual consistency.
                        </p>
                      </div>
                      <div className="flex flex-col items-center text-center p-4 rounded-lg border bg-card">
                        <div className="p-3 rounded-full bg-purple-50 border border-purple-100 mb-3">
                          <BookOpen className="h-6 w-6 text-purple-600" />
                        </div>
                        <h3 className="font-medium mb-2">Source Evaluation</h3>
                        <p className="text-sm text-muted-foreground">
                          We evaluate the credibility of the source based on its history and reputation.
                        </p>
                      </div>
                      <div className="flex flex-col items-center text-center p-4 rounded-lg border bg-card">
                        <div className="p-3 rounded-full bg-green-50 border border-green-100 mb-3">
                          <BarChart3 className="h-6 w-6 text-green-600" />
                        </div>
                        <h3 className="font-medium mb-2">Comprehensive Score</h3>
                        <p className="text-sm text-muted-foreground">
                          We combine multiple factors to generate a comprehensive credibility score.
                        </p>
                      </div>
                    </div>
                  </CardContent>
                </Card>
              </div>
            )}

            {activeTab === "analyze" && (
              <Card className="shadow-lg border-t-4 border-t-blue-500">
                <CardHeader>
                  <CardTitle className="text-2xl">Analyze News Content</CardTitle>
                  <CardDescription>Enter a URL or paste text content to analyze its credibility</CardDescription>
                </CardHeader>
                <CardContent>
                  <Tabs defaultValue="url" className="w-full">
                    <TabsList className="grid w-full grid-cols-2 mb-6">
                      <TabsTrigger value="url" className="flex items-center gap-2">
                        <LinkIcon className="h-4 w-4" />
                        Analyze URL
                      </TabsTrigger>
                      <TabsTrigger value="text" className="flex items-center gap-2">
                        <FileText className="h-4 w-4" />
                        Analyze Text
                      </TabsTrigger>
                    </TabsList>

                    <TabsContent value="url">
                      <div className="space-y-4">
                        <div className="space-y-2">
                          <label htmlFor="url-input" className="text-sm font-medium">
                            News Article URL
                          </label>
                          <div className="flex gap-2">
                            <Input
                              id="url-input"
                              placeholder="https://example.com/news-article"
                              value={url}
                              onChange={(e) => setUrl(e.target.value)}
                              className="flex-1"
                            />
                            <Button onClick={() => analyzeNews("url")} disabled={isAnalyzing || !url} className="gap-2">
                              {isAnalyzing ? (
                                <>
                                  <Loader2 className="h-4 w-4 animate-spin" />
                                  Analyzing...
                                </>
                              ) : (
                                <>
                                  <Search className="h-4 w-4" />
                                  Analyze
                                </>
                              )}
                            </Button>
                          </div>
                          <p className="text-xs text-muted-foreground">
                            Enter the full URL of a news article to analyze its credibility
                          </p>
                        </div>

                        <div className="bg-blue-50 dark:bg-blue-950 p-4 rounded-lg border border-blue-100 dark:border-blue-900">
                          <div className="flex items-start">
                            <Info className="h-5 w-5 text-blue-600 dark:text-blue-400 mt-0.5 mr-3 flex-shrink-0" />
                            <div>
                              <h4 className="font-medium text-blue-800 dark:text-blue-300 mb-1">
                                How URL Analysis Works
                              </h4>
                              <p className="text-sm text-blue-700 dark:text-blue-400">
                                When you submit a URL, our system will fetch the content, extract the text, and analyze
                                it for signs of misinformation using our advanced algorithms.
                              </p>
                            </div>
                          </div>
                        </div>
                      </div>
                    </TabsContent>

                    <TabsContent value="text">
                      <div className="space-y-4">
                        <div className="space-y-2">
                          <label htmlFor="text-input" className="text-sm font-medium">
                            News Article Text
                          </label>
                          <Textarea
                            id="text-input"
                            placeholder="Paste news article text here..."
                            className="min-h-[200px] resize-y"
                            value={text}
                            onChange={(e) => setText(e.target.value)}
                          />
                          <p className="text-xs text-muted-foreground">
                            Paste the full text of a news article to analyze its credibility
                          </p>
                        </div>

                        <div className="flex justify-end">
                          <Button onClick={() => analyzeNews("text")} disabled={isAnalyzing || !text} className="gap-2">
                            {isAnalyzing ? (
                              <>
                                <Loader2 className="h-4 w-4 animate-spin" />
                                Analyzing...
                              </>
                            ) : (
                              <>
                                <Search className="h-4 w-4" />
                                Analyze
                              </>
                            )}
                          </Button>
                        </div>

                        <div className="bg-blue-50 dark:bg-blue-950 p-4 rounded-lg border border-blue-100 dark:border-blue-900">
                          <div className="flex items-start">
                            <Info className="h-5 w-5 text-blue-600 dark:text-blue-400 mt-0.5 mr-3 flex-shrink-0" />
                            <div>
                              <h4 className="font-medium text-blue-800 dark:text-blue-300 mb-1">
                                How Text Analysis Works
                              </h4>
                              <p className="text-sm text-blue-700 dark:text-blue-400">
                                Our system analyzes the text for emotional language, clickbait tactics, factual
                                consistency, and other indicators of fake news.
                              </p>
                            </div>
                          </div>
                        </div>
                      </div>
                    </TabsContent>
                  </Tabs>
                </CardContent>
              </Card>
            )}

            {isAnalyzing && (
              <Card className="border-t-4 border-t-blue-500 shadow-lg">
                <CardContent className="pt-6">
                  <div className="flex flex-col items-center justify-center py-8">
                    <div className="relative mb-6">
                      <div className="absolute inset-0 flex items-center justify-center">
                        <Loader2 className="h-12 w-12 text-blue-500 animate-spin" />
                      </div>
                      <Newspaper className="h-16 w-16 text-muted-foreground opacity-20" />
                    </div>
                    <h3 className="text-xl font-medium mb-3">Analyzing content...</h3>
                    <p className="text-sm text-muted-foreground mb-6 text-center max-w-md">
                      Our AI is examining the article for signs of misinformation, checking source credibility,
                      analyzing language patterns, and comparing with known facts.
                    </p>
                    <Progress value={45} className="w-full max-w-md mb-2" />
                    <p className="text-xs text-muted-foreground">This may take a few moments</p>
                  </div>
                </CardContent>
              </Card>
            )}

            {error && (
              <Alert variant="destructive" className="border-t-4 border-t-red-500 shadow-lg">
                <AlertCircle className="h-4 w-4" />
                <AlertTitle>Error</AlertTitle>
                <AlertDescription>{error}</AlertDescription>
              </Alert>
            )}

            {activeTab === "results" && result && !isAnalyzing && (
              <div className="space-y-6">
                <Card
                  className={`border-t-4 ${
                    result.score > 70
                      ? "border-t-green-500"
                      : result.score > 40
                        ? "border-t-yellow-500"
                        : "border-t-red-500"
                  } shadow-lg`}
                >
                  <CardHeader>
                    <div className="flex items-center justify-between">
                      <CardTitle className="text-2xl">Analysis Results</CardTitle>
                      <div className={`p-2 rounded-full ${getStatusBg(result.score)} ${getStatusBorder(result.score)}`}>
                        {getStatusIcon(result.score)}
                      </div>
                    </div>
                    <CardDescription>Our AI-powered analysis of the provided content</CardDescription>
                  </CardHeader>
                  <CardContent className="space-y-6">
                    <div
                      className={`p-6 rounded-lg ${getStatusBg(result.score)} ${getStatusBorder(result.score)} border`}
                    >
                      <h3 className={`text-xl font-bold mb-2 ${getStatusText(result.score)}`}>{result.verdict}</h3>
                      <p className="text-gray-700 dark:text-gray-300">{result.explanation}</p>
                    </div>

                    <div>
                      <h3 className="text-lg font-medium mb-4 flex items-center">
                        <BarChart3 className="h-5 w-5 mr-2 text-blue-500" />
                        Credibility Score
                      </h3>
                      <div className="bg-gray-100 dark:bg-gray-800 p-6 rounded-lg">
                        <div className="flex items-center gap-4 mb-2">
                          <Progress value={result.score} className="flex-1 h-3" />
                          <span className={`text-2xl font-bold ${getStatusText(result.score)}`}>
                            {Math.round(result.score)}%
                          </span>
                        </div>
                        <div className="flex justify-between text-xs text-muted-foreground mt-1">
                          <span>Likely Fake</span>
                          <span>Questionable</span>
                          <span>Likely Legitimate</span>
                        </div>
                      </div>
                    </div>

                    <div>
                      <h3 className="text-lg font-medium mb-4 flex items-center">
                        <BarChart3 className="h-5 w-5 mr-2 text-blue-500" />
                        Analysis Factors
                      </h3>
                      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                        {result.factors.map((factor, index) => (
                          <Card key={index} className="overflow-hidden">
                            <div className={`h-1 ${getStatusColor(factor.score)}`}></div>
                            <CardContent className="pt-4">
                              <div className="flex justify-between items-center mb-2">
                                <h4 className="font-medium">{factor.name}</h4>
                                <Badge
                                  variant={
                                    factor.score > 70 ? "success" : factor.score > 40 ? "warning" : "destructive"
                                  }
                                >
                                  {Math.round(factor.score)}%
                                </Badge>
                              </div>
                              <Progress value={factor.score} className="h-2" />
                              <p className="text-xs text-muted-foreground mt-2">
                                {factor.score > 70
                                  ? "Strong indicators of credibility"
                                  : factor.score > 40
                                    ? "Some concerns detected"
                                    : "Significant issues detected"}
                              </p>
                            </CardContent>
                          </Card>
                        ))}
                      </div>
                    </div>

                    <div className="bg-blue-50 dark:bg-blue-950 p-4 rounded-lg border border-blue-100 dark:border-blue-900">
                      <div className="flex items-start">
                        <Info className="h-5 w-5 text-blue-600 dark:text-blue-400 mt-0.5 mr-3 flex-shrink-0" />
                        <div>
                          <h4 className="font-medium text-blue-800 dark:text-blue-300 mb-1">What to do next</h4>
                          <p className="text-sm text-blue-700 dark:text-blue-400 mb-2">
                            Based on our analysis, here are some recommended actions:
                          </p>
                          <ul className="text-sm text-blue-700 dark:text-blue-400 space-y-1 list-disc pl-5">
                            <li>Verify this information with other reputable sources</li>
                            <li>Check the publication date to ensure it's current</li>
                            <li>Look for the original source of any claims made</li>
                            <li>Consider the author's expertise and potential biases</li>
                          </ul>
                        </div>
                      </div>
                    </div>
                  </CardContent>
                  <CardFooter className="flex justify-between">
                    <Button variant="outline" onClick={() => setActiveTab("analyze")}>
                      Analyze Another
                    </Button>
                    <Button>Save Results</Button>
                  </CardFooter>
                </Card>

                <Card>
                  <CardHeader>
                    <CardTitle>Detailed Explanation</CardTitle>
                    <CardDescription>Understanding the factors in our analysis</CardDescription>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    <div className="space-y-2">
                      <h3 className="font-medium">Source Credibility</h3>
                      <p className="text-sm text-muted-foreground">
                        We evaluate the source based on its history of accurate reporting, transparency, and reputation.
                        Sources with established credibility receive higher scores.
                      </p>
                    </div>
                    <div className="space-y-2">
                      <h3 className="font-medium">Emotional Language</h3>
                      <p className="text-sm text-muted-foreground">
                        Objective reporting uses neutral language. We analyze the text for emotionally charged words
                        that may indicate an attempt to manipulate the reader's emotions.
                      </p>
                    </div>
                    <div className="space-y-2">
                      <h3 className="font-medium">Fact Consistency</h3>
                      <p className="text-sm text-muted-foreground">
                        We check for internal consistency and compare claims against known facts. Articles with
                        verifiable information score higher.
                      </p>
                    </div>
                    <div className="space-y-2">
                      <h3 className="font-medium">Clickbait Detection</h3>
                      <p className="text-sm text-muted-foreground">
                        Clickbait headlines often use sensationalist language to attract attention. We identify common
                        clickbait patterns and tactics.
                      </p>
                    </div>
                    <div className="space-y-2">
                      <h3 className="font-medium">Conspiracy Theories</h3>
                      <p className="text-sm text-muted-foreground">
                        We detect language patterns common in conspiracy theories, such as claims of cover-ups or secret
                        agendas without substantial evidence.
                      </p>
                    </div>
                  </CardContent>
                </Card>
              </div>
            )}

            {/* Auth Modal Overlay */}
            {showAuthModal && (
              <div className="fixed inset-0 bg-black/50 backdrop-blur-sm z-50 flex items-center justify-center p-4">
                <Card className="w-full max-w-md shadow-2xl border-t-4 border-t-blue-600">
                  <CardHeader className="text-center">
                    <div className="mx-auto bg-blue-100 p-3 rounded-full w-fit mb-4">
                      <Shield className="h-8 w-8 text-blue-600" />
                    </div>
                    <CardTitle className="text-2xl">Analysis Limit Reached</CardTitle>
                    <CardDescription>
                      You've used your 3 free analyses. Please log in or create an account to continue protecting yourself from misinformation.
                    </CardDescription>
                  </CardHeader>
                  <CardContent>
                    <Tabs defaultValue="login">
                      <TabsList className="grid w-full grid-cols-2 mb-4">
                        <TabsTrigger value="login">Login</TabsTrigger>
                        <TabsTrigger value="signup">Sign Up</TabsTrigger>
                      </TabsList>
                      <TabsContent value="login" className="space-y-4">
                        <div className="space-y-2">
                          <Input type="email" placeholder="Email address" />
                          <Input type="password" placeholder="Password" />
                        </div>
                        <Button className="w-full" onClick={() => { setIsLoggedIn(true); setShowAuthModal(false); }}>
                          Sign In
                        </Button>
                      </TabsContent>
                      <TabsContent value="signup" className="space-y-4">
                        <div className="space-y-2">
                          <Input type="text" placeholder="Full Name" />
                          <Input type="email" placeholder="Email address" />
                          <Input type="password" placeholder="Create Password" />
                        </div>
                        <Button className="w-full" onClick={() => { setIsLoggedIn(true); setShowAuthModal(false); }}>
                          Create Account
                        </Button>
                      </TabsContent>
                    </Tabs>
                  </CardContent>
                  <CardFooter className="flex flex-col space-y-2">
                    <div className="relative w-full py-2">
                      <div className="absolute inset-0 flex items-center"><Separator /></div>
                      <span className="relative bg-white px-2 text-xs text-muted-foreground mx-auto block w-fit">Or continue with</span>
                    </div>
                    <div className="grid grid-cols-2 gap-2 w-full">
                      <Button variant="outline">Google</Button>
                      <Button variant="outline">GitHub</Button>
                    </div>
                    <Button variant="ghost" size="sm" className="mt-4" onClick={() => setShowAuthModal(false)}>
                      Maybe Later
                    </Button>
                  </CardFooter>
                </Card>
              </div>
            )}
          </div>
        </div>
      </main>

      <footer className="bg-white dark:bg-gray-950 border-t border-gray-200 dark:border-gray-800 py-6">
        <div className="container mx-auto px-4">
          <div className="flex flex-col md:flex-row justify-between items-center">
            <div className="flex items-center space-x-2 mb-4 md:mb-0">
              <Shield className="h-5 w-5 text-blue-600 dark:text-blue-400" />
              <span className="font-bold text-gray-900 dark:text-white">Fake News Detector</span>
            </div>
            <div className="text-sm text-gray-500 dark:text-gray-400">
              © {new Date().getFullYear()} Fake News Detector. All rights reserved.
            </div>
            <div className="flex space-x-4 mt-4 md:mt-0">
              <Button variant="ghost" size="sm" onClick={() => window.open('/privacy', '_blank')}>
                Privacy Policy
              </Button>
              <Button variant="ghost" size="sm" onClick={() => window.open('/terms', '_blank')}>
                Terms of Service
              </Button>
              <Button variant="ghost" size="sm" onClick={() => window.open('/docs', '_blank')}>
                API Docs
              </Button>
              <Button variant="ghost" size="sm" className="flex items-center gap-1">
                <div className="h-2 w-2 rounded-full bg-green-500 animate-pulse" />
                System Status
              </Button>
            </div>
          </div>
        </div>
      </footer>
    </div>
  )
}
